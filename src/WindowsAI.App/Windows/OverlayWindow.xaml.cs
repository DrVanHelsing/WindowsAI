using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WindowsAI.Services;
using WindowsAI.Services.Interop;
using WindowsAI_App.ViewModels;
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

    public ChatViewModel ViewModel { get; }

    /// <summary>Native handle — used to attach the global hotkey.</summary>
    public IntPtr Hwnd { get; }

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

        _appWindow.IsShownInSwitchers = false;
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
        Title = "WindowsAI";

        ViewModel.Messages.CollectionChanged += OnMessagesChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    // ── Show / hide ─────────────────────────────────────────────────────────

    public async void ShowOverlay()
    {
        if (!_initializedModels)
        {
            _initializedModels = true;
            await ViewModel.InitializeAsync();
        }

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
            AmbientPulseStoryboard.Begin();
            SparkleSpinStoryboard.Begin();
        }
        else
        {
            AmbientPulseStoryboard.Stop();
            SparkleSpinStoryboard.Stop();
            AmbientBar.Opacity = 0.30;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (ViewModel.Messages.Count > 0)
            ResultsList.ScrollIntoView(ViewModel.Messages[^1]);
    }

    // ── Input ───────────────────────────────────────────────────────────────

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Escape:
                e.Handled = true;
                HideOverlay();
                break;

            case VirtualKey.Enter:
                var shift = Microsoft.UI.Input.InputKeyboardSource
                    .GetKeyStateForCurrentThread(VirtualKey.Shift)
                    .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
                if (shift) return;

                e.Handled = true;
                if (ViewModel.SendCommand.CanExecute(null))
                    ViewModel.SendCommand.Execute(null);
                break;
        }
    }
}
