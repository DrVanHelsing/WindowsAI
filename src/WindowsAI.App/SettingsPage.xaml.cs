using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Windows.UI.ViewManagement;
using WindowsAI_App.Controls;
using WindowsAI_App.ViewModels;

namespace WindowsAI_App;

/// <summary>Settings: appearance, local endpoint, and secure (DPAPI) cloud API-key management.</summary>
public sealed partial class SettingsPage : Page
{
    // Accent palette offered in Settings. null = follow the Windows accent color.
    private static readonly (string Name, string? Hex)[] AccentPresets =
    [
        ("Windows default", null),
        ("Electric Iris", "#6E7BFF"),
        ("Indigo", "#6366F1"),
        ("Blue", "#3B82F6"),
        ("Cyan", "#06B6D4"),
        ("Teal", "#14B8A6"),
        ("Green", "#22C55E"),
        ("Amber", "#F59E0B"),
        ("Orange", "#F97316"),
        ("Red", "#EF4444"),
        ("Pink", "#EC4899"),
        ("Violet", "#8B5CF6"),
    ];

    private readonly List<Button> _swatches = new();

    public SettingsViewModel ViewModel { get; }

    /// <summary>Assembly version shown on the About card.</summary>
    public string AppVersion { get; } =
        typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        InitializeComponent();
        BuildAccentSwatches();
    }

    // ── Accent swatches ──────────────────────────────────────────────────────

    private void BuildAccentSwatches()
    {
        var osAccent = new UISettings().GetColorValue(UIColorType.Accent);

        foreach (var (name, hex) in AccentPresets)
        {
            var color = hex is null ? osAccent : ParseHex(hex);

            // Selection ring (accent-tinted) shown only on the chosen swatch.
            var ring = new Ellipse
            {
                Width = 32,
                Height = 32,
                StrokeThickness = 2,
                Stroke = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                Opacity = 0,
            };
            var dot = new Ellipse
            {
                Width = 24,
                Height = 24,
                Fill = new SolidColorBrush(color),
            };
            // A small "follow Windows" glyph marks the default swatch.
            var content = new Grid();
            content.Children.Add(dot);
            if (hex is null)
            {
                content.Children.Add(new FontIcon
                {
                    FontSize = 11,
                    Glyph = "", // sync — "follows Windows"
                    Foreground = (Brush)Application.Current.Resources["AccentTextBrush"],
                });
            }
            content.Children.Add(ring);

            var button = new Button
            {
                Width = 38,
                Height = 38,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(19),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = content,
                Tag = hex,
            };
            ToolTipService.SetToolTip(button, name);
            button.Click += AccentSwatch_Click;
            _swatches.Add(button);
            AccentSwatchPanel.Children.Add(button);
        }

        UpdateSwatchSelection();
    }

    private async void AccentSwatch_Click(object sender, RoutedEventArgs e)
    {
        var hex = ((FrameworkElement)sender).Tag as string;
        await ViewModel.SetAccentAsync(hex);
        UpdateSwatchSelection();
    }

    /// <summary>Highlights the swatch matching the active accent (the ring ellipse, last child).</summary>
    private void UpdateSwatchSelection()
    {
        foreach (var button in _swatches)
        {
            var selected = string.Equals(button.Tag as string, ViewModel.AccentColor, StringComparison.OrdinalIgnoreCase);
            if (button.Content is Grid grid && grid.Children[^1] is Ellipse ring)
                ring.Opacity = selected ? 1 : 0;
        }
    }

    private static Color ParseHex(string hex)
    {
        var s = hex.TrimStart('#');
        var v = uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromArgb(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
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

    private async void ChangeVisionModel_Click(object sender, RoutedEventArgs e)
    {
        var id = await DialogService.PromptTextAsync(
            XamlRoot,
            "Vision model",
            "OpenRouter model id used for screen-capture questions (Alt+Shift+S). Must accept images.",
            initialText: ViewModel.VisionModelId,
            placeholder: "google/gemma-4-31b-it:free");

        if (id is not null)
        {
            ViewModel.VisionModelId = id;
            await ViewModel.SaveVisionModelAsync();
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

    // ── Web search (Google Programmable Search) ──────────────────────────────

    private async void ChangeWebSearchEngineId_Click(object sender, RoutedEventArgs e)
    {
        var id = await DialogService.PromptTextAsync(
            XamlRoot,
            "Search engine id",
            "The id (cx) of your Programmable Search Engine from programmablesearchengine.google.com.",
            initialText: ViewModel.WebSearchEngineId,
            placeholder: "a1b2c3d4e5f6g7h8i");

        if (id is not null)
            await ViewModel.SaveWebSearchEngineIdAsync(id);
    }

    private async void ManageWebSearchKey_Click(object sender, RoutedEventArgs e)
    {
        var key = await DialogService.PromptPasswordAsync(
            XamlRoot,
            "Web search API key",
            "Your Custom Search JSON API key from the Google Cloud console. Stored encrypted (DPAPI) on this PC only.",
            placeholder: "AIza…");

        if (key is not null)
            ViewModel.SaveWebSearchKey(key);
    }

    private void RemoveWebSearchKey_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearWebSearchKey();

    // ── Google sign-in (Vertex AI) ───────────────────────────────────────────

    private async void ChangeGoogleClientId_Click(object sender, RoutedEventArgs e)
    {
        var id = await DialogService.PromptTextAsync(
            XamlRoot,
            "OAuth client id",
            "The client id of your Google Cloud OAuth \"Desktop app\" client.",
            initialText: ViewModel.GoogleClientId,
            placeholder: "1234567890-abc.apps.googleusercontent.com");

        if (id is not null)
            await ViewModel.SaveGoogleClientIdAsync(id);
    }

    private async void ManageGoogleClientSecret_Click(object sender, RoutedEventArgs e)
    {
        var secret = await DialogService.PromptPasswordAsync(
            XamlRoot,
            "OAuth client secret",
            "The client secret from the same OAuth client. Stored encrypted (DPAPI) on this PC only.",
            placeholder: "GOCSPX-…");

        if (secret is not null)
            ViewModel.SaveGoogleClientSecret(secret);
    }

    private async void ChangeGoogleProject_Click(object sender, RoutedEventArgs e)
    {
        var project = await DialogService.PromptTextAsync(
            XamlRoot,
            "Google Cloud project id",
            "The project that has the Vertex AI API enabled.",
            initialText: ViewModel.GoogleProjectId,
            placeholder: "my-gcp-project");

        if (project is not null)
            await ViewModel.SaveGoogleProjectAsync(project);
    }

    private async void ChangeGoogleLocation_Click(object sender, RoutedEventArgs e)
    {
        var location = await DialogService.PromptTextAsync(
            XamlRoot,
            "Vertex AI region",
            "The region serving the model, e.g. us-central1 or global.",
            initialText: ViewModel.GoogleLocation,
            placeholder: "us-central1");

        if (location is not null)
            await ViewModel.SaveGoogleLocationAsync(location);
    }

    private async void SignInWithGoogle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await ViewModel.SignInWithGoogleAsync();
        }
        catch (Exception ex)
        {
            await DialogService.ConfirmAsync(
                XamlRoot,
                "Google sign-in failed",
                ex.Message,
                confirmText: "OK");
        }
    }

    private async void SignOutOfGoogle_Click(object sender, RoutedEventArgs e)
    {
        if (await DialogService.ConfirmAsync(
                XamlRoot, "Sign out of Google?", "The app will forget your Google sign-in on this PC.",
                confirmText: "Sign out", isDestructive: true))
        {
            await ViewModel.SignOutOfGoogleAsync();
        }
    }
}
