using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;
using WindowsAI.Core.Configuration;
using WindowsAI.Services;
using WindowsAI_App.Helpers;

namespace WindowsAI_App.Services;

/// <summary>
/// Keeps the app + overlay in lock-step with the Windows light/dark setting and accent color, live.
///
/// Tokens.xaml is a flat dictionary of shared <see cref="SolidColorBrush"/>/<see cref="GradientStop"/>
/// instances consumed almost entirely via <c>StaticResource</c>. Rather than convert every XAML file
/// to ThemeResource + ThemeDictionaries, this service mutates those shared brush instances in place —
/// every consumer updates instantly — and flips each window root's <see cref="FrameworkElement.RequestedTheme"/>
/// so the built-in WinUI control styles follow too. Accent-family brushes are re-derived from the OS
/// accent (<see cref="UISettings"/>); the brand identity gradient stays brand-colored on purpose.
///
/// <see cref="UISettings.ColorValuesChanged"/> fires for both accent and theme changes; we marshal back
/// to the UI thread and re-apply.
/// </summary>
public sealed class ThemeSyncService
{
    private readonly UISettings _ui = new();
    private readonly ISettingsStore _settings;

    public ThemeSyncService(ISettingsStore settings) => _settings = settings;

    private DispatcherQueue? _dispatcher;
    private readonly List<(FrameworkElement Root, GlassBackdropHandle? Backdrop, IntPtr BorderHwnd)> _windows = new();
    private readonly List<SolidReg> _solids = new();
    private readonly List<StopReg> _stops = new();
    private readonly List<AccentReg> _accents = new();

    private bool _started;
    private bool _isDark = true;
    private Color _accent;

    /// <summary>The theme currently applied (true = dark). Lets a window pick the right state when it registers.</summary>
    public bool IsDark => _isDark;

    /// <summary>Resolves token brushes, reads the current OS theme/accent, and subscribes to live changes. UI thread.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        BuildRegistrations();

        _ui.ColorValuesChanged += OnColorValuesChanged;
        ApplyFromSystem();
    }

    /// <summary>
    /// Brings <paramref name="root"/> (and its window backdrop, if any) under theme control and applies
    /// the current theme to it immediately. When <paramref name="borderHwnd"/> is supplied, the window's
    /// native Win11 border is tinted to the OS accent and re-tinted on every accent change.
    /// </summary>
    public void RegisterWindow(FrameworkElement root, GlassBackdropHandle? backdrop = null, IntPtr borderHwnd = default)
    {
        if (root is null) return;
        _windows.Add((root, backdrop, borderHwnd));
        ApplyToWindow(root, backdrop, _isDark);
        ApplyBorder(borderHwnd, _accent);
    }

    // ── System read / live updates ───────────────────────────────────────────

    private void OnColorValuesChanged(UISettings sender, object args)
        => _dispatcher?.TryEnqueue(ApplyFromSystem);

    /// <summary>
    /// Re-applies theme + accent after the user changes the appearance settings. Safe to call
    /// before <see cref="Start"/> (no-op until then). UI thread.
    /// </summary>
    public void Refresh()
    {
        if (_started)
            ApplyFromSystem();
    }

    private void ApplyFromSystem()
    {
        var appearance = _settings.Current.Appearance;

        // Theme heuristic: a bright foreground means a dark background → dark mode. The user can
        // override "follow Windows" by pinning Light or Dark in Settings.
        var fg = _ui.GetColorValue(UIColorType.Foreground);
        var systemDark = ((5 * fg.G) + (2 * fg.R) + fg.B) > (8 * 128);
        var isDark = appearance.Theme switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            _ => systemDark,
        };

        // Accent: a custom color (with light/dark variants derived from it) or the OS accent.
        Color accent, accentLight, accentDark;
        if (TryParseHexColor(appearance.AccentColor, out var custom))
        {
            accent = custom;
            accentLight = Lighten(custom, 0.4);
            accentDark = Darken(custom, 0.25);
        }
        else
        {
            accent = _ui.GetColorValue(UIColorType.Accent);
            accentLight = _ui.GetColorValue(UIColorType.AccentLight2);
            accentDark = _ui.GetColorValue(UIColorType.AccentDark1);
        }

        _isDark = isDark;
        _accent = accent;

        foreach (var r in _solids)
            r.Brush.Color = isDark ? r.Dark : r.Light;

        foreach (var s in _stops)
            s.Stop.Color = isDark ? s.Dark : s.Light;

        foreach (var a in _accents)
        {
            var baseColor = a.TextVariant ? (isDark ? accentLight : accentDark) : accent;
            a.Brush.Color = WithAlpha(baseColor, isDark ? a.AlphaDark : a.AlphaLight);
        }

        foreach (var (root, backdrop, borderHwnd) in _windows)
        {
            ApplyToWindow(root, backdrop, isDark);
            ApplyBorder(borderHwnd, accent);
        }
    }

    private static void ApplyToWindow(FrameworkElement root, GlassBackdropHandle? backdrop, bool isDark)
    {
        root.RequestedTheme = isDark ? ElementTheme.Dark : ElementTheme.Light;
        backdrop?.SetTheme(isDark);
    }

    /// <summary>Tints a window's native Win11 border to the OS accent. No-op when no hwnd was registered.</summary>
    private static void ApplyBorder(IntPtr borderHwnd, Color accent)
    {
        if (borderHwnd != IntPtr.Zero)
            WindowChrome.SetBorderColor(borderHwnd, accent.R, accent.G, accent.B);
    }

    // ── Token registrations ────────────────────────────────────────────────────

    private void BuildRegistrations()
    {
        // Text hierarchy: white-on-dark ⇄ black-on-light. (Mirrors Tokens.xaml v3.)
        Solid("TextPrimaryBrush", "#F4FFFFFF", "#E6000000");
        Solid("TextSecondaryBrush", "#A6FFFFFF", "#99000000");
        Solid("TextTertiaryBrush", "#6EFFFFFF", "#66000000");
        Solid("TextGhostBrush", "#38FFFFFF", "#33000000");

        // Liquid-glass fills: white veils on dark ⇄ black veils on light.
        Solid("GlassFillSubtleBrush", "#0AFFFFFF", "#0A000000");
        Solid("GlassFillBrush", "#14FFFFFF", "#12000000");
        Solid("GlassFillElevatedBrush", "#1FFFFFFF", "#1A000000");
        Solid("GlassHoverBrush", "#1EFFFFFF", "#14000000");
        Solid("GlassPressedBrush", "#29FFFFFF", "#1F000000");

        // Chat + chrome surfaces.
        Solid("UserBubbleFillBrush", "#B32E2A78", "#FFE7E6FF");
        Solid("CodeFillBrush", "#66000000", "#0F000000");
        Solid("CodeHeaderFillBrush", "#1AFFFFFF", "#0F000000");
        Solid("HairlineBrush", "#12FFFFFF", "#14000000");
        Solid("DialogSurfaceBrush", "#F21A1626", "#F7F6F5FB");
        Solid("OverlayScrimBrush", "#B30D0A1C", "#A6F4F3FA");
        Solid("SidebarFillBrush", "#590A0816", "#14000000");

        // Edge-lit strokes + specular sheen (gradient stops).
        Stops("GlassStrokeBrush",
            darks: new[] { "#5CFFFFFF", "#12FFFFFF", "#26FFFFFF" },
            lights: new[] { "#2E000000", "#0A000000", "#14000000" });
        Stops("GlassStrokeSubtleBrush",
            darks: new[] { "#33FFFFFF", "#0CFFFFFF", "#16FFFFFF" },
            lights: new[] { "#1F000000", "#08000000", "#0E000000" });
        Stops("GlassSheenBrush",
            darks: new[] { "#14FFFFFF", "#05FFFFFF", "#00FFFFFF" },
            lights: new[] { "#0EFFFFFF", "#03FFFFFF", "#00FFFFFF" });

        // Accent family — re-derived from the OS accent each apply.
        Accent("AccentBrush", 0xFF, 0xFF);
        Accent("AccentTextBrush", 0xFF, 0xFF, textVariant: true);
        Accent("AccentFillSubtleBrush", 0x1F, 0x24);
        Accent("AccentFillMediumBrush", 0x38, 0x40);
        Accent("AccentStrokeBrush", 0x4D, 0x59);
        Accent("InlineCodeFillBrush", 0x2E, 0x24);
        Accent("GlassFillAccentBrush", 0x1A, 0x16);
    }

    private void Solid(string key, string dark, string light)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is SolidColorBrush brush)
            _solids.Add(new SolidReg(brush, Parse(dark), Parse(light)));
    }

    private void Stops(string key, string[] darks, string[] lights)
    {
        if (!Application.Current.Resources.TryGetValue(key, out var v) || v is not LinearGradientBrush brush)
            return;
        for (var i = 0; i < brush.GradientStops.Count && i < darks.Length && i < lights.Length; i++)
            _stops.Add(new StopReg(brush.GradientStops[i], Parse(darks[i]), Parse(lights[i])));
    }

    private void Accent(string key, byte alphaDark, byte alphaLight, bool textVariant = false)
    {
        if (Application.Current.Resources.TryGetValue(key, out var v) && v is SolidColorBrush brush)
            _accents.Add(new AccentReg(brush, alphaDark, alphaLight, textVariant));
    }

    // ── Color helpers ──────────────────────────────────────────────────────────

    private static Color WithAlpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);

    /// <summary>Blends a color toward white by <paramref name="amount"/> (0–1) for the light variant.</summary>
    private static Color Lighten(Color c, double amount) => Color.FromArgb(
        c.A,
        (byte)(c.R + ((255 - c.R) * amount)),
        (byte)(c.G + ((255 - c.G) * amount)),
        (byte)(c.B + ((255 - c.B) * amount)));

    /// <summary>Blends a color toward black by <paramref name="amount"/> (0–1) for the dark variant.</summary>
    private static Color Darken(Color c, double amount) => Color.FromArgb(
        c.A,
        (byte)(c.R * (1 - amount)),
        (byte)(c.G * (1 - amount)),
        (byte)(c.B * (1 - amount)));

    /// <summary>Parses <c>#RRGGBB</c> (or <c>#AARRGGBB</c>) to an opaque accent color; false when null/blank/invalid.</summary>
    private static bool TryParseHexColor(string? hex, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
            return false;
        var s = hex.TrimStart('#');
        if ((s.Length != 6 && s.Length != 8) ||
            !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
            return false;

        color = s.Length == 8
            ? Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : Color.FromArgb(0xFF, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    /// <summary>Parses <c>#AARRGGBB</c> or <c>#RRGGBB</c> (opaque) to a <see cref="Color"/>.</summary>
    private static Color Parse(string hex)
    {
        var s = hex.TrimStart('#');
        var value = uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return s.Length == 8
            ? Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value)
            : Color.FromArgb(0xFF, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    private readonly record struct SolidReg(SolidColorBrush Brush, Color Dark, Color Light);
    private readonly record struct StopReg(GradientStop Stop, Color Dark, Color Light);
    private readonly record struct AccentReg(SolidColorBrush Brush, byte AlphaDark, byte AlphaLight, bool TextVariant);
}
