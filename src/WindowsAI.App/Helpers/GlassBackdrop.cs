using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WinRT;

namespace WindowsAI_App.Helpers;

/// <summary>
/// Applies the app's liquid-glass backdrop to a window: a hand-tuned acrylic
/// (deep indigo tint, real behind-window blur) pinned to the active state so the
/// glass never falls back to a flat solid when the window loses focus.
/// Falls back to MicaAlt where acrylic is unsupported.
/// </summary>
public static class GlassBackdrop
{
    /// <summary>
    /// Applies glass to <paramref name="window"/> and wires disposal to its Closed event.
    /// <paramref name="tintOpacity"/>/<paramref name="luminosityOpacity"/> tune readability:
    /// the overlay runs lighter (more desktop showing through) than the main app.
    /// </summary>
    public static void Apply(Window window, float tintOpacity, float luminosityOpacity)
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            return;
        }

        var configuration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = SystemBackdropTheme.Dark,
        };

        var controller = new DesktopAcrylicController
        {
            TintColor         = global::Windows.UI.Color.FromArgb(255, 16, 10, 32),
            TintOpacity       = tintOpacity,
            LuminosityOpacity = luminosityOpacity,
            FallbackColor     = global::Windows.UI.Color.FromArgb(255, 14, 10, 26),
        };
        controller.AddSystemBackdropTarget(window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        controller.SetSystemBackdropConfiguration(configuration);

        window.Closed += (_, _) => controller.Dispose();
    }
}
