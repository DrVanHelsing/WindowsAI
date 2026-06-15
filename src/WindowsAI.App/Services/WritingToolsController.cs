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
                return; // OS rejected the copy chord — nothing was read; clipboard is untouched.

            await Task.Delay(120); // let the focused app service the Ctrl+C
            var selection = await ClipboardHelper.TryGetTextAsync();

            if (string.IsNullOrWhiteSpace(selection))
                return; // Nothing selected (or the app didn't surface it).

            _window ??= CreateWindow();
            _window.ShowFor(selection, target, original);
            handedOff = true; // the window now owns restoring `original` after its paste.
        }
        finally
        {
            if (!handedOff && original is not null)
                ClipboardHelper.SetText(original);
            _busy = false;
        }
    }

    private WritingToolsWindow CreateWindow()
    {
        var window = new WritingToolsWindow(_selectionService, _writing);
        _registerWindowTheme(window);
        return window;
    }
}
