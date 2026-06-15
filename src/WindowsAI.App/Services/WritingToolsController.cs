using Microsoft.UI.Dispatching;
using WindowsAI.Core.Writing;
using WindowsAI.Services.Interop;
using WindowsAI.Services.Writing;
using WindowsAI_App.Helpers;
using WindowsAI_App.Windows;

namespace WindowsAI_App.Services;

/// <summary>
/// Drives the type-anywhere writing-tools flow. On the writing hotkey it captures the focused
/// app and the current selection (by borrowing the clipboard with a synthesized Ctrl+C), then
/// shows the <see cref="WritingToolsWindow"/> menu for that text. Owns the long-lived popup
/// window so the hotkey stays responsive across the session.
/// </summary>
public sealed class WritingToolsController
{
    private readonly TextSelectionService _selectionService;
    private readonly WritingToolsService _writing;
    private readonly DispatcherQueue _dispatcher;
    private readonly Action<WritingToolsWindow> _registerWindowTheme;

    private WritingToolsWindow? _window;
    private bool _busy;

    public WritingToolsController(
        TextSelectionService selectionService,
        WritingToolsService writing,
        DispatcherQueue dispatcher,
        Action<WritingToolsWindow> registerWindowTheme)
    {
        _selectionService = selectionService;
        _writing = writing;
        _dispatcher = dispatcher;
        _registerWindowTheme = registerWindowTheme;
    }

    /// <summary>Hotkey entry point. Marshals onto the UI thread, then captures + shows.</summary>
    public void Trigger() => _dispatcher.TryEnqueue(async () => await TriggerCoreAsync());

    private async Task TriggerCoreAsync()
    {
        // Re-entrancy guard: the copy round-trip is async; ignore repeat presses mid-capture.
        if (_busy)
            return;
        _busy = true;

        // The app whose selection we're transforming — focus returns here before pasting.
        var target = NativeMethods.GetForegroundWindow();

        // Borrow the clipboard to read the selection, remembering what to put back. Ownership of the
        // restore passes to the window once we hand it off; until then we restore on every exit path
        // so a failed copy never strands our borrowed contents on the user's clipboard.
        string? original = await ClipboardHelper.TryGetTextAsync();
        var handedOff = false;
        try
        {
            if (!_selectionService.SendCopy())
            {
                // OS rejected the copy chord (often a higher-integrity foreground window).
                ShowNotice("Couldn't send a copy command to the active window. Try running this app as administrator if the target app is elevated.");
                return;
            }

            await Task.Delay(150); // let the focused app service the Ctrl+C
            var selection = await ClipboardHelper.TryGetTextAsync();

            if (string.IsNullOrWhiteSpace(selection))
            {
                // Nothing was captured — usually no selection, or the app didn't surface one.
                ShowNotice("Select some text first, then press the writing-tools hotkey (Ctrl+Shift+Space).");
                return;
            }

            _window ??= CreateWindow();
            _window.ShowFor(selection, target, original);
            handedOff = true; // the window now owns restoring `original` after its paste.
        }
        catch (Exception ex)
        {
            // Never let the flow fail silently — surface it in the popup so it's diagnosable.
            ShowNotice($"Writing tools hit a problem: {ex.Message}");
        }
        finally
        {
            if (!handedOff && original is not null)
                ClipboardHelper.SetText(original);
            _busy = false;
        }
    }

    /// <summary>Shows the popup with a short message instead of the action menu (no selection / failure).</summary>
    private void ShowNotice(string message)
    {
        _window ??= CreateWindow();
        _window.ShowNotice(message);
    }

    private WritingToolsWindow CreateWindow()
    {
        var window = new WritingToolsWindow(_selectionService, _writing);
        _registerWindowTheme(window);
        return window;
    }
}
