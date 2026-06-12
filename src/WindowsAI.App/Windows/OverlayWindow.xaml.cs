using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WindowsAI.Core.History;
using WindowsAI.Core.Search;
using WindowsAI.Services;
using WindowsAI.Services.Interop;
using WindowsAI.Services.Search;
using WindowsAI_App.ViewModels;
using WinRT;
using WinRT.Interop;

namespace WindowsAI_App.Windows;

/// <summary>
/// Left-edge slide-in assistant panel. Full work-area height, 420 logical pixels wide.
/// Stays alive (hidden) for the whole session so the global hotkey and tray remain available.
/// </summary>
public sealed partial class OverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    private readonly AppWindow _appWindow;
    private bool _initializedModels;

    // ── Live launcher search state ──
    private readonly OverlaySearchService _search;
    private readonly OverlayRecentsStore _recents;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _debounce;
    private readonly List<SearchRowViewModel> _selectableRows = new();
    private CancellationTokenSource? _searchCts;
    private Task? _pendingSearch;
    private string _lastResultsQuery = string.Empty;
    private int _selectedIndex = -1;

    public ChatViewModel ViewModel { get; }

    /// <summary>Native handle — used to attach the global hotkey.</summary>
    public IntPtr Hwnd { get; }

    /// <summary>Root element whose RequestedTheme ThemeSyncService flips to track the OS light/dark setting.</summary>
    public FrameworkElement ThemeRoot => RootHost;

    public bool IsOverlayVisible => _appWindow.IsVisible;

    public OverlayWindow()
    {
        ViewModel = App.CreateChatViewModel();
        InitializeComponent();

        Hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow;

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = false;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // Win11 still draws a 1px light border around borderless windows — kill it.
        WindowChrome.RemoveWindowBorder(Hwnd);

        _appWindow.IsShownInSwitchers = false;

        // Fully transparent window: the desktop shows directly through; RootHost's
        // scrim (in XAML) provides the panel's tint and readability.
        SystemBackdrop = new Helpers.TransparentBackdrop();
        Title = "WindowsAI";

        ViewModel.Messages.CollectionChanged += OnMessagesChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;

        // The overlay has no RAG toggle: when a query routes to the AI it always gets the
        // local-file tools and decides itself whether to search this PC.
        ViewModel.UseLocalContext = true;

        _search = App.Services.GetRequiredService<OverlaySearchService>();
        _recents = App.Services.GetRequiredService<OverlayRecentsStore>();
        _debounce = DispatcherQueue.CreateTimer();
        _debounce.Interval = TimeSpan.FromMilliseconds(180);
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => _ = RunSearchAsync(SearchBox.Text.Trim());

        SearchBox.TextChanged += SearchBox_TextChanged;

        // Warm the apps-folder cache so the first keystroke doesn't pay the COM enumeration.
        _ = Task.Run(() => App.Services.GetRequiredService<AppCatalog>().Count);
    }

    // ── Show / hide ─────────────────────────────────────────────────────────

    public async void ShowOverlay()
    {
        if (!_initializedModels)
        {
            _initializedModels = true;
            await ViewModel.InitializeAsync();
        }

        await RefreshRecentsAsync();
        PositionForLeftEdge();

        // Reset slide position before showing so re-opens always slide in.
        RootSlide.X = -440;
        RootHost.Opacity = 0;

        _appWindow.Show();
        WindowChrome.BringToForeground(Hwnd);

        ShowStoryboard.Begin();
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
    }

    public void ToggleOverlay()
    {
        if (_appWindow.IsVisible) HideOverlay();
        else ShowOverlay();
    }

    public void HideOverlay()
    {
        if (_appWindow.IsVisible)
            DismissStoryboard.Begin();
    }

    private void DismissStoryboard_Completed(object sender, object e) => _appWindow.Hide();

    // ── Positioning ─────────────────────────────────────────────────────────

    private void PositionForLeftEdge()
    {
        var monitor = NativeMethods.MonitorFromWindow(Hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);

        var scale = GetDpiForWindow(Hwnd) / 96.0;
        int width = (int)(420 * scale);

        // Use the work area (excludes taskbar) so we don't overlap it.
        _appWindow.MoveAndResize(new RectInt32(
            info.rcWork.Left,
            info.rcWork.Top,
            width,
            info.rcWork.Bottom - info.rcWork.Top));
    }

    // ── ViewModel reactions ─────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChatViewModel.IsBusy)) return;

        if (ViewModel.IsBusy)
        {
            ThinkingFrame.Opacity = 1;
            ThinkingSpinStoryboard.Begin();
        }
        else
        {
            ThinkingFrame.Opacity = 0;
            ThinkingSpinStoryboard.Stop();
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateBodyVisibility();

        if (ViewModel.Messages.Count > 0)
            ResultsList.ScrollIntoView(ViewModel.Messages[^1]);
    }

    /// <summary>
    /// One body surface at a time: live results while a query is typed, otherwise the
    /// chat when a conversation is active, otherwise the empty state (recents + TRY).
    /// </summary>
    private void UpdateBodyVisibility()
    {
        var searching = SearchBox.Text.Trim().Length > 0;
        SearchPanel.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.Visibility = !searching && ViewModel.Messages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyPanel.Visibility = !searching && ViewModel.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        // With a conversation on screen the footer button carries it into the full app.
        OpenAppLabel.Text = ViewModel.Messages.Count > 0 ? "Continue in app" : "Open app";
    }

    // ── Live launcher search ────────────────────────────────────────────────

    private void SearchBox_TextChanged(object sender, Microsoft.UI.Xaml.Controls.TextChangedEventArgs e)
    {
        var text = SearchBox.Text.Trim();
        if (text.Length == 0)
        {
            _debounce.Stop();
            _searchCts?.Cancel();
            _lastResultsQuery = string.Empty;
            UpdateBodyVisibility();
            return;
        }

        UpdateBodyVisibility();
        _debounce.Stop();
        _debounce.Start();
    }

    private async Task RunSearchAsync(string query)
    {
        if (query.Length == 0)
            return;

        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();

        var task = _search.SearchAsync(query, cts.Token);
        _pendingSearch = task;
        try
        {
            var results = await task;

            // Stale reply (user kept typing) — a newer search owns the UI now.
            if (cts.IsCancellationRequested || query != SearchBox.Text.Trim())
                return;

            BuildRows(results);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            // Search failures must never take the overlay down — the Ask-AI path remains.
        }
    }

    private void BuildRows(OverlaySearchResults results)
    {
        var rows = new List<SearchRowViewModel>();
        void AddSection(string header, IEnumerable<SearchHit> hits)
        {
            var list = hits.ToList();
            if (list.Count == 0)
                return;
            rows.Add(SearchRowViewModel.Header(header));
            rows.AddRange(list.Select(SearchRowViewModel.ForHit));
        }

        if (results.BestMatch is { } best)
            AddSection("BEST MATCH", [best]);
        AddSection("APPS", results.Apps);
        AddSection("FILES", results.Files);
        AddSection("SETTINGS", results.Settings);
        rows.Add(SearchRowViewModel.AskAi(results.Query));

        SearchRowsList.ItemsSource = rows;
        _selectableRows.Clear();
        _selectableRows.AddRange(rows.Where(r => r.IsSelectable));
        _lastResultsQuery = results.Query;

        // Default selection mirrors Enter's routing: prompt-like or empty → Ask AI,
        // otherwise the best match (first selectable row).
        var askAiDefault = results.IsEmpty || PromptClassifier.IsLikelyPrompt(results.Query);
        Select(askAiDefault ? _selectableRows.Count - 1 : 0);
    }

    private void Select(int index)
    {
        if (_selectedIndex >= 0 && _selectedIndex < _selectableRows.Count)
            _selectableRows[_selectedIndex].IsSelected = false;

        _selectedIndex = _selectableRows.Count == 0 ? -1 : Math.Clamp(index, 0, _selectableRows.Count - 1);
        if (_selectedIndex >= 0)
            _selectableRows[_selectedIndex].IsSelected = true;
    }

    private void MoveSelection(int delta)
    {
        if (_selectableRows.Count == 0)
            return;
        // Wrap around like the Start menu does.
        var next = (_selectedIndex + delta + _selectableRows.Count) % _selectableRows.Count;
        Select(next);
    }

    /// <summary>Enter / Go: open what's selected — or hand the query to the AI.</summary>
    private async Task RouteCurrentQueryAsync()
    {
        var text = SearchBox.Text.Trim();

        // A dropped file with no text still asks the AI (it substitutes a default question).
        if (text.Length == 0)
        {
            if (ViewModel.HasPendingAttachments)
                SendToAi();
            return;
        }

        // Attachments mean the user wants to ask about the file, not launch a match.
        if (ViewModel.HasPendingAttachments)
        {
            SendToAi();
            return;
        }

        // If results for the current text are still in flight, let them land so Enter
        // acts on what the user sees (or would see a frame later).
        if (_lastResultsQuery != text && _pendingSearch is { } pending)
        {
            try { await pending; } catch { /* routed below regardless */ }
        }

        var selected = _lastResultsQuery == text && _selectedIndex >= 0
            ? _selectableRows[_selectedIndex]
            : null;

        if (selected is { Hit: { } hit })
        {
            Launch(hit);
            return;
        }

        // Ask-AI row selected, no results yet, or prompt-like text → the AI.
        SendToAi();
    }

    // ── Drag-and-drop: drop Explorer files onto the overlay to attach them ──────

    private void RootHost_DragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;

        e.AcceptedOperation = global::Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
        if (e.DragUIOverride is { } ui)
        {
            ui.Caption = "Attach to question";
            ui.IsContentVisible = false;
            ui.IsGlyphVisible = false;
        }
        ShowDropAffordance(true);
    }

    private void RootHost_DragLeave(object sender, DragEventArgs e) => ShowDropAffordance(false);

    private async void RootHost_Drop(object sender, DragEventArgs e)
    {
        ShowDropAffordance(false);
        if (!e.DataView.Contains(global::Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            return;

        // Hold the deferral until the async item walk completes (the DataView is only valid
        // for the duration of the drop).
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<global::Windows.Storage.StorageFile>()
                             .Select(f => f.Path)
                             .Where(p => !string.IsNullOrEmpty(p))
                             .ToList();
            if (paths.Count > 0)
                ViewModel.AddAttachments(paths);
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void ShowDropAffordance(bool show)
    {
        DropAffordance.Visibility = Visibility.Visible;
        DropAffordance.Opacity = show ? 1 : 0;
        if (!show)
            DropAffordance.Visibility = Visibility.Collapsed;
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is PendingAttachment attachment)
            ViewModel.RemoveAttachmentCommand.Execute(attachment);
    }

    private void SendToAi()
    {
        if (ViewModel.SendCommand.CanExecute(null))
            ViewModel.SendCommand.Execute(null); // clears Input → search panel yields to chat
    }

    private void Launch(SearchHit hit)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(hit.Target)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // Target gone/unlaunchable — keep the overlay calm.
            return;
        }

        // Quick-access memory: the item, and the query that found it (fire-and-forget).
        var query = SearchBox.Text.Trim();
        _ = _recents.RecordItemAsync(hit);
        if (query.Length > 0)
            _ = _recents.RecordQueryAsync(query);

        SearchBox.Text = string.Empty;
        HideOverlay();
    }

    private void SearchRow_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not SearchRowViewModel row)
            return;

        if (row.Hit is { } hit)
            Launch(hit);
        else if (row.IsAskAi)
            SendToAi();
    }

    private async void Go_Click(object sender, RoutedEventArgs e)
        => await RouteCurrentQueryAsync();

    // ── Empty state: recents + quick actions ────────────────────────────────

    /// <summary>
    /// Loads the empty-state quick access: recent AI conversations, recently launched
    /// items, and recent search queries.
    /// </summary>
    private async Task RefreshRecentsAsync()
    {
        try
        {
            var store = App.Services.GetRequiredService<IConversationStore>();
            var recents = (await store.ListAsync()).Take(3).ToList();
            RecentsList.ItemsSource = recents.Select(c => new ConversationSummary(c)).ToList();
            RecentsLabel.Visibility = recents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            // Launched items are replayed through the same row VM the live list uses.
            var items = await _recents.GetItemsAsync();
            RecentItemsList.ItemsSource = items
                .Select(i => SearchRowViewModel.ForHit(new SearchHit(i.Kind, i.Name, i.Detail, i.Target)))
                .ToList();
            RecentItemsLabel.Visibility = items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

            var queries = await _recents.GetQueriesAsync();
            RecentQueriesList.ItemsSource = queries.ToList();
            RecentQueriesLabel.Visibility = queries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            // Recents are decoration — never block the overlay on history I/O.
        }
    }

    /// <summary>Re-opens a recently launched item.</summary>
    private void RecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SearchRowViewModel { Hit: { } hit })
            Launch(hit);
    }

    /// <summary>Drops a recent query back into the box, re-running the live search.</summary>
    private void RecentQuery_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string query)
            return;

        SearchBox.Text = query;
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectionStart = SearchBox.Text.Length;
    }

    /// <summary>Opens a recent conversation in the full app and dismisses the overlay.</summary>
    private async void Recent_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not string id)
            return;

        var app = (App)Application.Current;
        app.ShowMainApp();
        await App.GetService<ChatViewModel>().LoadConversationAsync(id);
        app.AppShell?.ShowChat();
        HideOverlay();
    }

    /// <summary>Quick-action chip: drop its prompt stub into the search box.</summary>
    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Input = (string)((FrameworkElement)sender).Tag;
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectionStart = SearchBox.Text.Length;
    }

    /// <summary>
    /// "Open app" — or, when a conversation is on screen, "Continue in app": opens the
    /// shell and loads the same session into it (it was persisted after each exchange).
    /// </summary>
    private async void OpenApp_Click(object sender, RoutedEventArgs e)
    {
        var app = (App)Application.Current;
        app.ShowMainApp();

        if (ViewModel.Messages.Count > 0)
        {
            await App.GetService<ChatViewModel>().LoadConversationAsync(ViewModel.CurrentConversationId);
            app.AppShell?.ShowChat();
        }

        HideOverlay();
    }

    // ── Input ───────────────────────────────────────────────────────────────

    private async void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;
                // First Esc backs out of the query, second dismisses the overlay.
                if (SearchBox.Text.Length > 0)
                    SearchBox.Text = string.Empty;
                else
                    HideOverlay();
                break;

            case VirtualKey.Down:
                e.Handled = true;
                MoveSelection(+1);
                break;

            case VirtualKey.Up:
                e.Handled = true;
                MoveSelection(-1);
                break;

            case VirtualKey.Enter:
                var shift = Microsoft.UI.Input.InputKeyboardSource
                    .GetKeyStateForCurrentThread(VirtualKey.Shift)
                    .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
                if (shift) return;

                e.Handled = true;
                await RouteCurrentQueryAsync();
                break;
        }
    }
}
