using WindowsAI.Core.Search;

namespace WindowsAI.Services.Search;

/// <summary>
/// Curated Windows settings pages searchable by name/keyword, deep-linked via
/// <c>ms-settings:</c> URIs (the same mechanism Windows Search uses). Static table —
/// there is no public API to enumerate Settings search terms, so we mirror the
/// most-reached pages.
/// </summary>
public static class SettingsCatalog
{
    private sealed record Entry(string Name, string Uri, string Keywords);

    private static readonly Entry[] Entries =
    [
        new("Display", "ms-settings:display", "screen resolution brightness scale monitor hdr"),
        new("Night light", "ms-settings:nightlight", "blue light filter warm"),
        new("Bluetooth & devices", "ms-settings:bluetooth", "pair headphones speaker"),
        new("Wi-Fi", "ms-settings:network-wifi", "wireless internet network"),
        new("Ethernet", "ms-settings:network-ethernet", "lan wired network"),
        new("Airplane mode", "ms-settings:network-airplanemode", "flight wireless off"),
        new("VPN", "ms-settings:network-vpn", "virtual private network"),
        new("Mobile hotspot", "ms-settings:network-mobilehotspot", "tether share internet"),
        new("Sound", "ms-settings:sound", "volume audio speaker output input"),
        new("Microphone privacy", "ms-settings:privacy-microphone", "mic access"),
        new("Camera privacy", "ms-settings:privacy-webcam", "webcam access"),
        new("Notifications", "ms-settings:notifications", "alerts banners do not disturb"),
        new("Focus", "ms-settings:quiethours", "focus assist do not disturb dnd quiet"),
        new("Power & battery", "ms-settings:powersleep", "sleep battery saver screen timeout"),
        new("Storage", "ms-settings:storagesense", "disk space cleanup temporary files"),
        new("Multitasking", "ms-settings:multitasking", "snap windows virtual desktops"),
        new("Default apps", "ms-settings:defaultapps", "browser file associations"),
        new("Optional features", "ms-settings:optionalfeatures", "windows features add remove"),
        new("Themes", "ms-settings:themes", "wallpaper appearance"),
        new("Background", "ms-settings:personalization-background", "wallpaper desktop picture"),
        new("Lock screen", "ms-settings:lockscreen", "screensaver"),
        new("Colors", "ms-settings:personalization-colors", "dark mode light mode accent"),
        new("Taskbar", "ms-settings:taskbar", "system tray icons alignment"),
        new("Start", "ms-settings:personalization-start", "start menu pins layout"),
        new("Fonts", "ms-settings:fonts", "typeface install font"),
        new("Installed apps", "ms-settings:appsfeatures", "uninstall programs add remove apps features"),
        new("Startup apps", "ms-settings:startupapps", "boot autostart launch"),
        new("Your account", "ms-settings:yourinfo", "microsoft account profile"),
        new("Sign-in options", "ms-settings:signinoptions", "pin password hello fingerprint face"),
        new("Email & accounts", "ms-settings:emailandaccounts", "work school account"),
        new("Date & time", "ms-settings:dateandtime", "clock timezone"),
        new("Language & region", "ms-settings:regionlanguage", "locale keyboard layout language pack"),
        new("Typing", "ms-settings:typing", "autocorrect text suggestions keyboard"),
        new("Game Mode", "ms-settings:gaming-gamemode", "gaming performance"),
        new("Accessibility", "ms-settings:easeofaccess", "ease of access narrator magnifier contrast"),
        new("Privacy & security", "ms-settings:privacy", "permissions telemetry"),
        new("Windows Update", "ms-settings:windowsupdate", "updates check for updates patch"),
        new("Recovery", "ms-settings:recovery", "reset this pc restore advanced startup"),
        new("About", "ms-settings:about", "pc name specs windows version rename"),
        new("Printers & scanners", "ms-settings:printers", "print scan"),
        new("Mouse", "ms-settings:mousetouchpad", "pointer cursor speed"),
        new("Touchpad", "ms-settings:devices-touchpad", "gestures scroll tap"),
    ];

    public static IReadOnlyList<SearchHit> Search(string query, int maxResults)
    {
        query = query.Trim();
        if (query.Length < 2)
            return [];

        return Entries
            .Where(e => e.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                     || e.Keywords.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .Take(maxResults)
            .Select(e => new SearchHit(SearchHitKind.Setting, e.Name, "System settings", e.Uri))
            .ToList();
    }
}
