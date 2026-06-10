using CommunityToolkit.Mvvm.ComponentModel;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

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

    [ObservableProperty]
    public partial string OllamaEndpoint { get; set; }

    [ObservableProperty]
    public partial string OpenRouterStatus { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string GeminiStatus { get; set; } = string.Empty;

    public SettingsViewModel(ISettingsStore settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;

        OllamaEndpoint = _settings.Current.LocalModel.Endpoint;
        RefreshStatuses();
    }

    public async Task SaveOllamaEndpointAsync()
    {
        _settings.Current.LocalModel.Endpoint = OllamaEndpoint.Trim();
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
        OpenRouterStatus = Describe(_secrets.Get(SecretKeys.OpenRouterApiKey));
        GeminiStatus = Describe(_secrets.Get(SecretKeys.GeminiApiKey));
    }

    private static string Describe(string? key)
        => string.IsNullOrWhiteSpace(key) ? "Not set" : "Configured ✓";
}
