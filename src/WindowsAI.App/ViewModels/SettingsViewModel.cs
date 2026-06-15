using CommunityToolkit.Mvvm.ComponentModel;
using WindowsAI.Core.Auth;
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
    private readonly GoogleOAuthService _googleAuth;
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

    /// <summary>OAuth client id for the user's Google "Desktop app" client.</summary>
    [ObservableProperty]
    public partial string GoogleClientId { get; set; }

    /// <summary>Google Cloud project id with the Vertex AI API enabled.</summary>
    [ObservableProperty]
    public partial string GoogleProjectId { get; set; }

    /// <summary>Vertex AI region (e.g. us-central1, global).</summary>
    [ObservableProperty]
    public partial string GoogleLocation { get; set; }

    [ObservableProperty]
    public partial string WebSearchStatus { get; set; } = string.Empty;

    /// <summary>True once a Tavily API key is present.</summary>
    [ObservableProperty]
    public partial bool WebSearchConfigured { get; set; }

    [ObservableProperty]
    public partial string GoogleStatus { get; set; } = string.Empty;

    /// <summary>True once the user has signed in with Google — drives the Sign in/out buttons.</summary>
    [ObservableProperty]
    public partial bool GoogleConnected { get; set; }

    public SettingsViewModel(
        ISettingsStore settings, ISecretStore secrets, ThemeSyncService theme, GoogleOAuthService googleAuth)
    {
        _settings = settings;
        _secrets = secrets;
        _theme = theme;
        _googleAuth = googleAuth;

        OllamaEndpoint = _settings.Current.LocalModel.Endpoint;
        VisionModelId = _settings.Current.OpenRouter.VisionModelId;
        GoogleClientId = _settings.Current.GoogleCloud.ClientId;
        GoogleProjectId = _settings.Current.GoogleCloud.ProjectId;
        GoogleLocation = _settings.Current.GoogleCloud.Location;

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

    // ── Web search (Tavily) ──────────────────────────────────────────────────

    public void SaveWebSearchKey(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            _secrets.Set(SecretKeys.TavilyApiKey, key.Trim());
        RefreshStatuses();
    }

    public void ClearWebSearchKey()
    {
        _secrets.Delete(SecretKeys.TavilyApiKey);
        RefreshStatuses();
    }

    // ── Google sign-in (Vertex AI) ───────────────────────────────────────────

    public async Task SaveGoogleClientIdAsync(string clientId)
    {
        GoogleClientId = clientId.Trim();
        _settings.Current.GoogleCloud.ClientId = GoogleClientId;
        await _settings.SaveAsync();
        RefreshStatuses();
    }

    public void SaveGoogleClientSecret(string secret)
    {
        if (!string.IsNullOrWhiteSpace(secret))
            _secrets.Set(SecretKeys.GoogleClientSecret, secret.Trim());
        RefreshStatuses();
    }

    public async Task SaveGoogleProjectAsync(string projectId)
    {
        GoogleProjectId = projectId.Trim();
        _settings.Current.GoogleCloud.ProjectId = GoogleProjectId;
        await _settings.SaveAsync();
        RefreshStatuses();
    }

    public async Task SaveGoogleLocationAsync(string location)
    {
        GoogleLocation = location.Trim();
        _settings.Current.GoogleCloud.Location = GoogleLocation;
        await _settings.SaveAsync();
    }

    /// <summary>Runs the browser consent flow. Throws if the OAuth client isn't configured or consent fails.</summary>
    public async Task SignInWithGoogleAsync()
    {
        await _googleAuth.SignInAsync();
        RefreshStatuses();
    }

    public async Task SignOutOfGoogleAsync()
    {
        await _googleAuth.SignOutAsync();
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

        GoogleConnected = _googleAuth.IsSignedIn;
        GoogleStatus = DescribeGoogle();

        var searchKey = _secrets.Get(SecretKeys.TavilyApiKey);
        WebSearchConfigured = !string.IsNullOrWhiteSpace(searchKey);
        WebSearchStatus = WebSearchConfigured ? "Configured ✓" : "Not set";
    }

    /// <summary>Human-readable Google sign-in state for the Settings card.</summary>
    private string DescribeGoogle()
    {
        if (_googleAuth.IsSignedIn)
            return "Signed in ✓";

        var hasClient = !string.IsNullOrWhiteSpace(_settings.Current.GoogleCloud.ClientId)
            && !string.IsNullOrWhiteSpace(_secrets.Get(SecretKeys.GoogleClientSecret));
        return hasClient ? "Ready to sign in" : "Add OAuth client to begin";
    }

    private static string Describe(string? key)
        => string.IsNullOrWhiteSpace(key) ? "Not set" : "Configured ✓";
}
