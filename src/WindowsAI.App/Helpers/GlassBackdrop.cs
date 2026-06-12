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
    // Acrylic tints per theme: a deep indigo for dark, a bright near-white indigo for light.
    internal static readonly global::Windows.UI.Color DarkTint = global::Windows.UI.Color.FromArgb(255, 16, 10, 32);
    internal static readonly global::Windows.UI.Color LightTint = global::Windows.UI.Color.FromArgb(255, 244, 243, 250);

    /// <summary>
    /// Applies glass to <paramref name="window"/> and wires disposal to its Closed event. Returns a
    /// handle whose <see cref="GlassBackdropHandle.SetTheme"/> flips the acrylic tint + theme live so
    /// the backdrop tracks Windows light/dark alongside the rest of the chrome.
    /// <paramref name="tintOpacity"/>/<paramref name="luminosityOpacity"/> tune readability:
    /// the overlay runs lighter (more desktop showing through) than the main app.
    /// </summary>
    public static GlassBackdropHandle Apply(Window window, float tintOpacity, float luminosityOpacity)
    {
        if (!DesktopAcrylicController.IsSupported())
        {
            window.SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };
            return new GlassBackdropHandle(null, null);
        }

        var configuration = new SystemBackdropConfiguration
        {
            IsInputActive = true,
            Theme = SystemBackdropTheme.Dark,
        };

        var controller = new DesktopAcrylicController
        {
            TintColor         = DarkTint,
            TintOpacity       = tintOpacity,
            LuminosityOpacity = luminosityOpacity,
            FallbackColor     = global::Windows.UI.Color.FromArgb(255, 14, 10, 26),
        };
        controller.AddSystemBackdropTarget(window.As<Microsoft.UI.Composition.ICompositionSupportsSystemBackdrop>());
        controller.SetSystemBackdropConfiguration(configuration);

        window.Closed += (_, _) => controller.Dispose();

        return new GlassBackdropHandle(controller, configuration);
    }
}

/// <summary>
/// Live control surface over a window's acrylic backdrop. <see cref="SetTheme"/> swaps the tint
/// color and backdrop theme so the glass follows the Windows light/dark setting. A no-op when the
/// window fell back to Mica (which already tracks the element theme).
/// </summary>
public sealed class GlassBackdropHandle
{
    private readonly DesktopAcrylicController? _controller;
    private readonly SystemBackdropConfiguration? _configuration;

    internal GlassBackdropHandle(DesktopAcrylicController? controller, SystemBackdropConfiguration? configuration)
    {
        _controller = controller;
        _configuration = configuration;
    }

    /// <summary>Flips the acrylic tint + backdrop theme to match dark/light. Safe to call any time.</summary>
    public void SetTheme(bool isDark)
    {
        if (_controller is null || _configuration is null)
            return;

        _configuration.Theme = isDark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        _controller.TintColor = isDark ? GlassBackdrop.DarkTint : GlassBackdrop.LightTint;
    }
}
