using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WindowsAI_App.Helpers;

/// <summary>
/// A SystemBackdrop that paints the window with a fully transparent composition brush,
/// making the window genuinely see-through (the desktop shows directly behind XAML
/// content — no blur, no tint). Readability comes from a translucent scrim drawn in
/// XAML, so the panel blends with translucency-themed shells (e.g. Windhawk mods).
/// </summary>
public sealed partial class TransparentBackdrop : SystemBackdrop
{
    // The backdrop target consumes Windows.UI.Composition brushes, which require the
    // system compositor — and that requires a Windows.System.DispatcherQueue on this
    // thread (WinUI 3 only provides the Microsoft.UI one). Created once per process.
    private static global::Windows.UI.Composition.Compositor? _compositor;
    private static object? _dispatcherQueueController;

    private global::Windows.UI.Composition.CompositionColorBrush? _brush;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);

        EnsureSystemDispatcherQueue();
        _compositor ??= new global::Windows.UI.Composition.Compositor();
        _brush = _compositor.CreateColorBrush(global::Windows.UI.Color.FromArgb(0, 0, 0, 0));
        connectedTarget.SystemBackdrop = _brush;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        base.OnTargetDisconnected(disconnectedTarget);
        disconnectedTarget.SystemBackdrop = null;
        _brush?.Dispose();
        _brush = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        internal int dwSize;
        internal int threadType;
        internal int apartmentType;
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        [In, Out, MarshalAs(UnmanagedType.IUnknown)] ref object? dispatcherQueueController);

    private static void EnsureSystemDispatcherQueue()
    {
        if (global::Windows.System.DispatcherQueue.GetForCurrentThread() is not null)
            return;
        if (_dispatcherQueueController is not null)
            return;

        var options = new DispatcherQueueOptions
        {
            dwSize = Marshal.SizeOf<DispatcherQueueOptions>(),
            threadType = 2,    // DQTYPE_THREAD_CURRENT
            apartmentType = 2, // DQTAT_COM_STA
        };
        _ = CreateDispatcherQueueController(options, ref _dispatcherQueueController);
    }
}
