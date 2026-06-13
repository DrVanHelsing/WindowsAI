using CommunityToolkit.Mvvm.ComponentModel;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;
using WindowsAI_App.Services;

namespace WindowsAI_App.ViewModels;

/// <summary>
/// Backs the Settings page: local endpoint plus secure (DPAPI) API-key management for
/// the opt-in cloud providers. Keys are written through <see cref="ISecretStore"/> and
/// never surfaced back to the UI in plaintext.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;
    private readonly ThemeSyncService _theme;
    private bool _initializing;

    [ObservableProperty]
    public partial string OllamaEndpoint { get; set; }

    /// <summary>OpenRouter model used for the screen-capture "ask about this region" flow (must support vision).</summary>
    [ObservableProperty]
    public partial string VisionModelId { get; set; }

    /// <summary>Light/dark selection bound to the theme RadioButtons (0 = Windows default, 1 = Light, 2 = Dark).</summary>
    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    /// <summary>The active accent override as <c>#RRGGBB</c>, or null when following Windows.</summary>
    public string? AccentColor { get; private set; }

    [ObservableProperty]
    public partial string OpenRouterStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GeminiStatus { get; set; } = string.Empty;

    /// <summary>True once a key is stored — drives the "Remove" button + "Add/Update" wording.</summary>
    [ObservableProperty]
    public partial bool OpenRouterConfigured { get; set; }

    [ObservableProperty]
    public partial bool GeminiConfigured { get; set; }

    public SettingsViewModel(ISettingsStore settings, ISecretStore secrets, ThemeSyncService theme)
    {
        _settings = settings;
        _secrets = secrets;
        _theme = theme;

        OllamaEndpoint = _settings.Current.LocalModel.Endpoint;
        VisionModelId = _settings.Current.OpenRouter.VisionModelId;

        _initializing = true;
        ThemeIndex = (int)_settings.Current.Appearance.Theme;
        _initializing = false;
        AccentColor = _settings.Current.Appearance.AccentColor;

        RefreshStatuses();
    }

    /// <summary>Persists the light/dark choice and re-applies it live across every window.</summary>
    partial void OnThemeIndexChanged(int value)
    {
        if (_initializing)
            return;
        _settings.Current.Appearance.Theme = (ThemeMode)value;
        _ = _settings.SaveAsync();
        _theme.Refresh();
    }

    /// <summary>Sets the accent override (null = follow Windows), persists it, and re-applies live.</summary>
    public async Task SetAccentAsync(string? hex)
    {
        AccentColor = hex;
        _settings.Current.Appearance.AccentColor = hex;
        await _settings.SaveAsync();
        _theme.Refresh();
    }

    public async Task SaveOllamaEndpointAsync()
    {
        _settings.Current.LocalModel.Endpoint = OllamaEndpoint.Trim();
        await _settings.SaveAsync();
    }

    public async Task SaveVisionModelAsync()
    {
        _settings.Current.OpenRouter.VisionModelId = VisionModelId.Trim();
        await _settings.SaveAsync();
    }

    public void SaveOpenRouterKey(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            _secrets.Set(SecretKeys.OpenRouterApiKey, key.Trim());
        RefreshStatuses();
    }

    public void ClearOpenRouterKey()
    {
        _secrets.Delete(SecretKeys.OpenRouterApiKey);
        RefreshStatuses();
    }

    public void SaveGeminiKey(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            _secrets.Set(SecretKeys.GeminiApiKey, key.Trim());
        RefreshStatuses();
    }

    public void ClearGeminiKey()
    {
        _secrets.Delete(SecretKeys.GeminiApiKey);
        RefreshStatuses();
    }

    private void RefreshStatuses()
    {
        var openRouter = _secrets.Get(SecretKeys.OpenRouterApiKey);
        var gemini = _secrets.Get(SecretKeys.GeminiApiKey);

        OpenRouterConfigured = !string.IsNullOrWhiteSpace(openRouter);
        GeminiConfigured = !string.IsNullOrWhiteSpace(gemini);
        OpenRouterStatus = Describe(openRouter);
        GeminiStatus = Describe(gemini);
    }

    private static string Describe(string? key)
        => string.IsNullOrWhiteSpace(key) ? "Not set" : "Configured ✓";
}
