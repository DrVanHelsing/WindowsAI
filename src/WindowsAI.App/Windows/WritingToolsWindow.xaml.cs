using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WindowsAI.Core.Writing;
using WindowsAI.Services;
using WindowsAI.Services.Interop;
using WindowsAI.Services.Writing;
using WindowsAI_App.Helpers;
using WinRT.Interop;

namespace WindowsAI_App.Windows;

/// <summary>
/// The type-anywhere writing-tools popup. Summoned by the writing hotkey over a text selection
/// in any app: it shows the action menu, runs the chosen transform against the active model, and
/// (on Replace) pastes the result back over the selection in the originating app. Kept alive
/// (hidden) for the session like the overlay so the hotkey stays responsive.
/// </summary>
public sealed partial class WritingToolsWindow : Window
{
    // Logical card size; multiplied by the cursor monitor's scale when positioned.
    private const int CardWidth = 360;
    private const int CardHeight = 440;

    private readonly AppWindow _appWindow;
    private readonly TextSelectionService _selectionService;
    private readonly WritingToolsService _writing;

    private IntPtr _targetWindow;
    private string _selection = string.Empty;
    private string? _originalClipboard;
    private string _result = string.Empty;
    private CancellationTokenSource? _cts;

    public IntPtr Hwnd { get; }

    /// <summary>Root element ThemeSyncService flips to track the OS light/dark setting.</summary>
    public FrameworkElement ThemeRoot => RootHost;

    public WritingToolsWindow(TextSelectionService selectionService, WritingToolsService writing)
    {
        _selectionService = selectionService;
        _writing = writing;

        InitializeComponent();

        Hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow;

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        WindowChrome.RoundWithBorder(Hwnd, 0x6E, 0x7B, 0xFF);
        _appWindow.IsShownInSwitchers = false;
        SystemBackdrop = new Helpers.TransparentBackdrop();
        Title = "WindowsAI Writing Tools";

        BuildActionButtons();
        RootHost.KeyDown += RootHost_KeyDown;
    }

    /// <summary>
    /// Shows the menu for a captured selection. <paramref name="targetWindow"/> is the app that
    /// had focus (restored before pasting); <paramref name="originalClipboard"/> is whatever the
    /// user had on the clipboard before we borrowed it (restored after pasting).
    /// </summary>
    public void ShowFor(string selection, IntPtr targetWindow, string? originalClipboard)
    {
        _cts?.Cancel();
        _selection = selection;
        _targetWindow = targetWindow;
        _originalClipboard = originalClipboard;
        _result = string.Empty;

        SelectionPreview.Text = selection.Trim();
        ShowActionsState();

        PositionNearCursor();
        RootHost.Opacity = 0;
        _appWindow.Show();
        WindowChrome.BringToForeground(Hwnd);
        ShowStoryboard.Begin();
    }

    /// <summary>
    /// Shows the popup with a short message in place of the action menu — used when there's no
    /// selection to act on or the capture failed, so the hotkey gives visible feedback instead of
    /// silently doing nothing.
    /// </summary>
    public void ShowNotice(string message)
    {
        _cts?.Cancel();
        _selection = string.Empty;
        _result = string.Empty;
        SelectionPreview.Text = string.Empty;
        NoticeText.Text = message;
        ShowNoticeState();

        PositionNearCursor();
        RootHost.Opacity = 0;
        _appWindow.Show();
        WindowChrome.BringToForeground(Hwnd);
        ShowStoryboard.Begin();
    }

    private void Hide()
    {
        _cts?.Cancel();
        _appWindow.Hide();
    }

    // ── State toggles ───────────────────────────────────────────────────────

    private void ShowActionsState()
    {
        PreviewBorder.Visibility = Visibility.Visible;
        ActionsScroller.Visibility = Visibility.Visible;
        ResultPanel.Visibility = Visibility.Collapsed;
        NoticePanel.Visibility = Visibility.Collapsed;
    }

    private void ShowResultState()
    {
        PreviewBorder.Visibility = Visibility.Visible;
        ActionsScroller.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        NoticePanel.Visibility = Visibility.Collapsed;
    }

    private void ShowNoticeState()
    {
        PreviewBorder.Visibility = Visibility.Collapsed;
        ActionsScroller.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Collapsed;
        NoticePanel.Visibility = Visibility.Visible;
    }

    // ── Action menu ───────────────────────────────────────────────────────

    private void BuildActionButtons()
    {
        foreach (var action in WritingAction.All)
        {
            var icon = new FontIcon
            {
                FontSize = 15,
                Glyph = action.Glyph,
                Foreground = (Brush)Application.Current.Resources["AccentTextBrush"],
            };
            var label = new TextBlock { Text = action.Label, VerticalAlignment = VerticalAlignment.Center };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            content.Children.Add(icon);
            content.Children.Add(label);

            var button = new Button
            {
                Content = content,
                Style = (Style)RootHost.Resources["WritingActionButtonStyle"],
                Tag = action,
            };
            button.Click += ActionButton_Click;
            ActionsPanel.Children.Add(button);
        }
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is WritingAction action)
            await RunActionAsync(action);
    }

    private async Task RunActionAsync(WritingAction action)
    {
        ShowResultState();
        SetResultBusy(true);
        ResultText.Text = string.Empty;
        _result = string.Empty;

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();

        var sb = new StringBuilder();
        try
        {
            await foreach (var delta in _writing.StreamAsync(action, _selection, cts.Token))
            {
                if (cts.IsCancellationRequested)
                    return;
                sb.Append(delta);
                ResultText.Text = sb.ToString();
            }

            if (cts.IsCancellationRequested)
                return;

            _result = WritingPrompt.CleanResult(sb.ToString());
            ResultText.Text = _result.Length == 0 ? "(no response)" : _result;
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer action or dismissed.
        }
        catch (Exception ex)
        {
            ResultText.Text = $"⚠ {ex.Message}";
        }
        finally
        {
            if (!cts.IsCancellationRequested)
                SetResultBusy(false);
        }
    }

    private void SetResultBusy(bool busy)
    {
        ThinkingRow.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        ReplaceButton.IsEnabled = !busy && _result.Length > 0;
        CopyButton.IsEnabled = !busy && _result.Length > 0;
        BackButton.IsEnabled = !busy;
    }

    // ── Result actions ───────────────────────────────────────────────────────

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        ShowActionsState();
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_result.Length == 0)
            return;
        ClipboardHelper.SetText(_result);
        Hide();
    }

    private async void Replace_Click(object sender, RoutedEventArgs e)
    {
        if (_result.Length == 0)
            return;

        // Put the result on the clipboard, hand focus back to the originating app, paste over the
        // selection, then restore the user's previous clipboard contents. The clipboard restore runs
        // in a finally so a failed focus/paste never strands the user's data on the clipboard.
        ClipboardHelper.SetText(_result);
        _appWindow.Hide();

        try
        {
            if (!await TryRestoreTargetFocusAsync())
                return; // couldn't return focus — don't paste into the wrong window.

            if (!_selectionService.SendPaste())
                return; // OS rejected the paste chord — leave the original selection intact.

            await Task.Delay(150); // let the target app service the paste before we swap the clipboard back.
        }
        finally
        {
            if (_originalClipboard is not null)
                ClipboardHelper.SetText(_originalClipboard);
            else
                ClipboardHelper.Clear();
        }
    }

    /// <summary>
    /// Restores focus to the originating app and confirms it actually took before we paste — pasting
    /// into the wrong (or our own) window would corrupt unrelated content. Retries the focus once.
    /// </summary>
    private async Task<bool> TryRestoreTargetFocusAsync()
    {
        if (_targetWindow == IntPtr.Zero)
            return false;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            NativeMethods.SetForegroundWindow(_targetWindow);
            await Task.Delay(60);
            if (NativeMethods.GetForegroundWindow() == _targetWindow)
                return true;
        }

        return false;
    }

    private void RootHost_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Hide();
        }
    }

    // ── Positioning ───────────────────────────────────────────────────────

    private void PositionNearCursor()
    {
        NativeMethods.GetCursorPos(out var cursor);
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MONITORINFO { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        NativeMethods.GetMonitorInfo(monitor, ref info);

        var scale = NativeMethods.GetDpiForMonitor(monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0
            ? dpiX / 96.0
            : 1.0;

        int w = (int)(CardWidth * scale);
        int h = (int)(CardHeight * scale);
        int gap = (int)(18 * scale);

        // Default: just below-right of the cursor; flip onto the work area when it would overflow.
        int x = cursor.X;
        int y = cursor.Y + gap;
        if (x + w > info.rcWork.Right) x = info.rcWork.Right - w;
        if (x < info.rcWork.Left) x = info.rcWork.Left;
        if (y + h > info.rcWork.Bottom) y = cursor.Y - gap - h;
        if (y < info.rcWork.Top) y = info.rcWork.Top;

        _appWindow.MoveAndResize(new RectInt32(x, y, w, h));
    }
}
