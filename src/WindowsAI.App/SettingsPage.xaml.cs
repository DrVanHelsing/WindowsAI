using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAI_App.Controls;
using WindowsAI_App.ViewModels;

namespace WindowsAI_App;

/// <summary>Settings: local endpoint and secure (DPAPI) cloud API-key management, all via modals.</summary>
public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    /// <summary>Assembly version shown on the About card.</summary>
    public string AppVersion { get; } =
        typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
    }

    private async void ChangeEndpoint_Click(object sender, RoutedEventArgs e)
    {
        var endpoint = await DialogService.PromptTextAsync(
            XamlRoot,
            "Ollama endpoint",
            "Where your local Ollama server is listening.",
            initialText: ViewModel.OllamaEndpoint,
            placeholder: "http://localhost:11434");

        if (endpoint is not null)
        {
            ViewModel.OllamaEndpoint = endpoint;
            await ViewModel.SaveOllamaEndpointAsync();
        }
    }

    private async void ManageOpenRouterKey_Click(object sender, RoutedEventArgs e)
    {
        var key = await DialogService.PromptPasswordAsync(
            XamlRoot,
            "OpenRouter API key",
            "Paste your key from openrouter.ai. It's stored encrypted (DPAPI) on this PC only.",
            placeholder: "sk-or-…");

        if (key is not null)
            ViewModel.SaveOpenRouterKey(key);
    }

    private void RemoveOpenRouterKey_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearOpenRouterKey();

    private async void ManageGeminiKey_Click(object sender, RoutedEventArgs e)
    {
        var key = await DialogService.PromptPasswordAsync(
            XamlRoot,
            "Gemini API key",
            "Paste your key from Google AI Studio. It's stored encrypted (DPAPI) on this PC only.",
            placeholder: "AIza…");

        if (key is not null)
            ViewModel.SaveGeminiKey(key);
    }

    private void RemoveGeminiKey_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearGeminiKey();
}
