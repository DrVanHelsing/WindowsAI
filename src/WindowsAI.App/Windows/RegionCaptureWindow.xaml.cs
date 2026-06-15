using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using WindowsAI.Core.Providers;
using WindowsAI.Services;
using WindowsAI.Services.Capture;
using WindowsAI.Services.Interop;
using WinRT.Interop;

namespace WindowsAI_App.Windows;

/// <summary>
/// Visual-intelligence region capture. Summoned by the visual hotkey: it shows a frozen, dimmed
/// screenshot of the monitor under the cursor, lets the user drag a region to spotlight, then crops
/// it and asks a configurable OpenRouter vision model about it — streaming the answer in an ask card.
/// Kept alive (hidden) for the session like the overlay so the hotkey stays responsive.
/// </summary>
public sealed partial class RegionCaptureWindow : Window
{
    /// <summary>Below this drag size (DIPs) a release is treated as a stray click, not a selection.</summary>
    private const double MinSelection = 8;

    private readonly AppWindow _appWindow;
    private readonly VisionService _vision;

    private CapturedScreen? _capture;
    private bool _selecting;
    private Point _start;
    private Rect _selection;
    private byte[]? _cropBytes;
    private CancellationTokenSource? _cts;

    public IntPtr Hwnd { get; }

    /// <summary>Root element ThemeSyncService flips to track the OS light/dark setting.</summary>
    public FrameworkElement ThemeRoot => RootHost;

    public RegionCaptureWindow(VisionService vision)
    {
        _vision = vision;

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

        WindowChrome.RemoveWindowBorder(Hwnd);
        _appWindow.IsShownInSwitchers = false;
        SystemBackdrop = new Helpers.TransparentBackdrop();
        Title = "WindowsAI Visual Intelligence";

        Overlay.SizeChanged += (_, _) => LayoutScrims();

        // Esc cancels regardless of which control holds focus.
        var esc = new KeyboardAccelerator { Key = VirtualKey.Escape };
        esc.Invoked += (_, args) => { args.Handled = true; Dismiss(); };
        RootHost.KeyboardAccelerators.Add(esc);
    }

    // ── Show / hide ─────────────────────────────────────────────────────────

    /// <summary>Shows the capture surface over a freshly grabbed monitor screenshot.</summary>
    public void ShowFor(CapturedScreen capture)
    {
        _capture?.Dispose();
        _capture = capture;
        _cts?.Cancel();

        // Window covers the captured monitor exactly (physical pixels).
        _appWindow.MoveAndResize(new RectInt32(
            capture.Bounds.X, capture.Bounds.Y, capture.Bounds.Width, capture.Bounds.Height));

        ScreenImage.Source = new BitmapImage(new Uri(capture.FullImagePath));

        // Reset to the "drag to select" state.
        ResetSelectionState();
        AskOverlay.Visibility = Visibility.Collapsed;
        AnswerText.Text = string.Empty;
        QuestionBox.Text = string.Empty;
        HintBanner.Visibility = Visibility.Visible;

        RootHost.Opacity = 0;
        _appWindow.Show();
        WindowChrome.BringToForeground(Hwnd);
        // Focus the root so the Esc accelerator fires even in the drag-select state (no child focused).
        RootHost.Focus(FocusState.Programmatic);
        ShowStoryboard.Begin();
    }

    private void ResetSelectionState()
    {
        _selecting = false;
        _selection = default;
        SelectionBox.Visibility = Visibility.Collapsed;
        SizeHintCard.Visibility = Visibility.Collapsed;
        FullScrim.Visibility = Visibility.Visible;
        ScrimTop.Visibility = ScrimBottom.Visibility = ScrimLeft.Visibility = ScrimRight.Visibility = Visibility.Collapsed;
        LayoutScrims();
    }

    private void Dismiss()
    {
        _cts?.Cancel();
        _appWindow.Hide();
        _capture?.Dispose();
        _capture = null;
        ScreenImage.Source = null;
        ThumbImage.Source = null;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Dismiss();

    // Clicking the dim scrim outside the ask card dismisses; the card swallows its own clicks so
    // interacting with the textbox/buttons doesn't bubble up and close the window.
    private void AskOverlay_PointerPressed(object sender, PointerRoutedEventArgs e) => Dismiss();

    private void AskCard_PointerPressed(object sender, PointerRoutedEventArgs e) => e.Handled = true;

    // ── Selection ───────────────────────────────────────────────────────────

    private void Overlay_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (AskOverlay.Visibility == Visibility.Visible)
            return;

        _selecting = true;
        _start = e.GetCurrentPoint(Overlay).Position;
        Overlay.CapturePointer(e.Pointer);

        FullScrim.Visibility = Visibility.Collapsed;
        ScrimTop.Visibility = ScrimBottom.Visibility = ScrimLeft.Visibility = ScrimRight.Visibility = Visibility.Visible;
        SelectionBox.Visibility = Visibility.Visible;
        SizeHintCard.Visibility = Visibility.Visible;
    }

    private void Overlay_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_selecting)
            return;

        var p = e.GetCurrentPoint(Overlay).Position;
        _selection = new Rect(
            Math.Min(_start.X, p.X),
            Math.Min(_start.Y, p.Y),
            Math.Abs(p.X - _start.X),
            Math.Abs(p.Y - _start.Y));

        LayoutSelection();
    }

    private void Overlay_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_selecting)
            return;

        _selecting = false;
        Overlay.ReleasePointerCapture(e.Pointer);

        if (_selection.Width < MinSelection || _selection.Height < MinSelection)
        {
            // Stray click — go back to the full-screen prompt.
            ResetSelectionState();
            return;
        }

        CaptureSelection();
    }

    /// <summary>Crops the selected region to a PNG and reveals the ask card.</summary>
    private void CaptureSelection()
    {
        if (_capture is null)
            return;

        var scale = _capture.Bounds.Scale;
        var path = _capture.SaveCrop(
            (int)(_selection.X * scale),
            (int)(_selection.Y * scale),
            (int)(_selection.Width * scale),
            (int)(_selection.Height * scale));

        try { _cropBytes = File.ReadAllBytes(path); }
        catch { _cropBytes = null; }

        ThumbImage.Source = new BitmapImage(new Uri(path));

        HintBanner.Visibility = Visibility.Collapsed;
        AnswerText.Text = string.Empty;
        AskOverlay.Visibility = Visibility.Visible;
        QuestionBox.Focus(FocusState.Programmatic);
    }

    // ── Geometry ──────────────────────────────────────────────────────────────

    private void LayoutScrims()
    {
        FullScrim.Width = Overlay.ActualWidth;
        FullScrim.Height = Overlay.ActualHeight;
        if (SelectionBox.Visibility == Visibility.Visible)
            LayoutSelection();
    }

    private void LayoutSelection()
    {
        var (x, y, w, h) = (_selection.X, _selection.Y, _selection.Width, _selection.Height);
        var (cw, ch) = (Overlay.ActualWidth, Overlay.ActualHeight);

        Place(SelectionBox, x, y, w, h);

        // Four scrims tile the area outside the selection, leaving it bright.
        Place(ScrimTop, 0, 0, cw, y);
        Place(ScrimBottom, 0, y + h, cw, Math.Max(0, ch - (y + h)));
        Place(ScrimLeft, 0, y, x, h);
        Place(ScrimRight, x + w, y, Math.Max(0, cw - (x + w)), h);

        var scale = _capture?.Bounds.Scale ?? 1.0;
        SizeHint.Text = $"{(int)(w * scale)} × {(int)(h * scale)}";
        // Park the readout just under the selection (or above when near the bottom edge).
        var hintY = y + h + 6;
        if (hintY + 24 > ch) hintY = y - 28;
        Canvas.SetLeft(SizeHintCard, x);
        Canvas.SetTop(SizeHintCard, Math.Max(0, hintY));
    }

    private static void Place(FrameworkElement el, double x, double y, double w, double h)
    {
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
        el.Width = Math.Max(0, w);
        el.Height = Math.Max(0, h);
    }

    // ── Ask ───────────────────────────────────────────────────────────────────

    private void QuestionBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            _ = AskAsync();
        }
    }

    private void Ask_Click(object sender, RoutedEventArgs e) => _ = AskAsync();

    private async Task AskAsync()
    {
        if (_cropBytes is null)
            return;

        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();

        SetBusy(true);
        AnswerText.Text = string.Empty;

        var sb = new StringBuilder();
        try
        {
            await foreach (var delta in _vision.StreamAsync(_cropBytes, "image/png", QuestionBox.Text, cts.Token))
            {
                if (cts.IsCancellationRequested)
                    return;
                sb.Append(delta);
                AnswerText.Text = sb.ToString();
            }

            if (cts.IsCancellationRequested)
                return;

            if (sb.Length == 0)
                AnswerText.Text = "(no response)";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AnswerText.Text = $"⚠ {ex.Message}";
        }
        finally
        {
            if (!cts.IsCancellationRequested)
                SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        ThinkingRow.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        AskButton.IsEnabled = !busy;
        QuestionBox.IsEnabled = !busy;
    }
}
