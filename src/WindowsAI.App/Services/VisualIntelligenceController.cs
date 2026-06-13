using Microsoft.UI.Dispatching;
using WindowsAI.Core.Providers;
using WindowsAI.Services.Capture;
using WindowsAI_App.Windows;

namespace WindowsAI_App.Services;

/// <summary>
/// Drives the visual-intelligence flow. On the visual hotkey it grabs a frozen screenshot of the
/// monitor under the cursor, then shows the <see cref="RegionCaptureWindow"/> so the user can
/// spotlight a region and ask a vision model about it. Owns the long-lived capture window so the
/// hotkey stays responsive across the session.
/// </summary>
public sealed class VisualIntelligenceController
{
    private readonly ScreenCaptureService _capture;
    private readonly VisionService _vision;
    private readonly DispatcherQueue _dispatcher;
    private readonly Action<RegionCaptureWindow> _registerWindowTheme;

    private RegionCaptureWindow? _window;
    private bool _busy;

    public VisualIntelligenceController(
        ScreenCaptureService capture,
        VisionService vision,
        DispatcherQueue dispatcher,
        Action<RegionCaptureWindow> registerWindowTheme)
    {
        _capture = capture;
        _vision = vision;
        _dispatcher = dispatcher;
        _registerWindowTheme = registerWindowTheme;
    }

    /// <summary>Hotkey entry point. Marshals onto the UI thread, then captures + shows.</summary>
    public void Trigger() => _dispatcher.TryEnqueue(TriggerCore);

    private void TriggerCore()
    {
        // Re-entrancy guard: ignore repeat presses while the capture surface is up.
        if (_busy)
            return;
        _busy = true;
        try
        {
            // Grab the screen *before* our window appears so the selection is drawn against a
            // still image and we never capture our own dimming overlay.
            var screen = _capture.CaptureMonitorUnderCursor();

            _window ??= CreateWindow();
            _window.ShowFor(screen);
        }
        finally
        {
            _busy = false;
        }
    }

    private RegionCaptureWindow CreateWindow()
    {
        var window = new RegionCaptureWindow(_vision);
        _registerWindowTheme(window);
        return window;
    }
}
