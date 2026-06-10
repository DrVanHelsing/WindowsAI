using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAI_App.ViewModels;

namespace WindowsAI_App;

/// <summary>Settings: local endpoint and secure (DPAPI) cloud API-key management.</summary>
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

    private async void SaveEndpoint_Click(object sender, RoutedEventArgs e)
        => await ViewModel.SaveOllamaEndpointAsync();

    private void SaveOpenRouterKey_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveOpenRouterKey(OpenRouterKeyBox.Password);
        OpenRouterKeyBox.Password = string.Empty;
    }

    private void ClearOpenRouterKey_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearOpenRouterKey();

    private void SaveGeminiKey_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.SaveGeminiKey(GeminiKeyBox.Password);
        GeminiKeyBox.Password = string.Empty;
    }

    private void ClearGeminiKey_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearGeminiKey();
}
