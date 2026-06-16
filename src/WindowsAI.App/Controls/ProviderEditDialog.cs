using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Providers;
using WindowsAI.Core.Security;

namespace WindowsAI_App.Controls;

/// <summary>The connection a user authored in <see cref="ProviderEditDialog"/>, plus any secrets entered.</summary>
public sealed record ProviderEditResult(ProviderConnection Connection, string? ApiKey, string? ClientSecret);

/// <summary>
/// A single, on-brand dialog for adding or editing a provider connection — replacing the old
/// per-field modal prompts. For a new provider it offers a preset gallery that pre-fills the form;
/// the user can also pick "Custom". Inline <b>Test connection</b> and <b>Fetch models</b> use
/// <see cref="ProviderProbe"/> for immediate feedback. Built in code (like <see cref="DialogService"/>)
/// so it shares the glass ContentDialog style and avoids XAML-compiler pitfalls. All edits happen on
/// a draft clone, so cancelling never mutates live settings.
/// </summary>
public static class ProviderEditDialog
{
    private const string ProbeId = "__probe";

    public static async Task<ProviderEditResult?> ShowAsync(
        XamlRoot root, ProviderProbe probe, ISecretStore secrets, ProviderConnection? existing)
    {
        var isEdit = existing is not null;
        var draft = Clone(existing) ?? new ProviderConnection { Kind = ProviderKind.OpenAiCompatible };

        var presetCombo = new ComboBox
        {
            Header = "Start from",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = ProviderPresets.All.Select(p => p.DisplayName).ToList(),
            Visibility = isEdit ? Visibility.Collapsed : Visibility.Visible,
        };
        var nameBox = new TextBox { Header = "Name", Text = draft.DisplayName, PlaceholderText = "e.g. My OpenAI" };
        var baseUrlBox = new TextBox { Header = "Base URL", Text = draft.BaseUrl, PlaceholderText = "https://api.example.com/v1" };
        var keyBox = new PasswordBox { Header = "API key (stored encrypted on this PC)", PlaceholderText = isEdit ? "leave blank to keep current" : "paste your key" };
        var clientIdBox = new TextBox { Header = "OAuth client id", Text = draft.OAuthClientId ?? "" };
        var clientSecretBox = new PasswordBox { Header = "OAuth client secret", PlaceholderText = isEdit ? "leave blank to keep current" : "GOCSPX-…" };
        var projectBox = new TextBox { Header = "Google Cloud project id", Text = draft.ProjectId ?? "" };
        var regionBox = new TextBox { Header = "Vertex region", Text = string.IsNullOrWhiteSpace(draft.Location) ? "global" : draft.Location };
        var visionCheck = new CheckBox { Content = "Supports image input (vision)", IsChecked = draft.SupportsVision };
        var imageCheck = new CheckBox { Content = "Supports image generation", IsChecked = draft.SupportsImageGeneration };
        var modelCombo = new ComboBox
        {
            Header = "Default model",
            IsEditable = true,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = draft.Models.Select(m => m.Id).ToList(),
            Text = draft.DefaultModelId ?? "",
        };
        var fetchButton = new Button { Content = "Fetch models" };
        var testButton = new Button { Content = "Test connection" };
        var statusText = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 2, 0, 0) };

        var panel = new StackPanel { Spacing = 10, MinWidth = 380 };
        foreach (var c in new UIElement[]
                 {
                     presetCombo, nameBox, baseUrlBox, keyBox, clientIdBox, clientSecretBox,
                     projectBox, regionBox, visionCheck, imageCheck, modelCombo,
                 })
            panel.Children.Add(c);
        var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        buttonRow.Children.Add(fetchButton);
        buttonRow.Children.Add(testButton);
        panel.Children.Add(buttonRow);
        panel.Children.Add(statusText);

        void ApplyKind()
        {
            var isVertex = draft.Kind == ProviderKind.GoogleVertex;
            var isOllama = draft.Kind == ProviderKind.Ollama;
            var isLocalOpenAi = draft.Kind == ProviderKind.OpenAiCompatible && IsLocalEndpoint(baseUrlBox.Text);

            baseUrlBox.Visibility = isVertex ? Visibility.Collapsed : Visibility.Visible;
            keyBox.Visibility = (isVertex || isOllama || isLocalOpenAi) ? Visibility.Collapsed : Visibility.Visible;
            clientIdBox.Visibility = clientSecretBox.Visibility = projectBox.Visibility = regionBox.Visibility =
                isVertex ? Visibility.Visible : Visibility.Collapsed;
            imageCheck.Visibility = draft.Kind == ProviderKind.OpenAiCompatible ? Visibility.Visible : Visibility.Collapsed;
            visionCheck.Visibility = isOllama ? Visibility.Collapsed : Visibility.Visible;
        }

        presetCombo.SelectionChanged += (_, _) =>
        {
            if (presetCombo.SelectedIndex < 0)
                return;
            var preset = ProviderPresets.All[presetCombo.SelectedIndex];
            draft.Kind = preset.Kind;
            nameBox.Text = preset.DisplayName;
            baseUrlBox.Text = preset.BaseUrl;
            if (preset.Kind == ProviderKind.GoogleVertex) regionBox.Text = "global";
            modelCombo.ItemsSource = preset.SuggestedModels.ToList();
            modelCombo.Text = preset.SuggestedModels.FirstOrDefault() ?? "";
            visionCheck.IsChecked = preset.SupportsVision;
            imageCheck.IsChecked = preset.SupportsImageGeneration;
            keyBox.PlaceholderText = string.IsNullOrEmpty(preset.KeyPlaceholder) ? "paste your key" : preset.KeyPlaceholder;
            ApplyKind();
        };
        baseUrlBox.TextChanged += (_, _) => ApplyKind();
        ApplyKind();

        void Snapshot()
        {
            draft.DisplayName = nameBox.Text.Trim();
            draft.BaseUrl = baseUrlBox.Text.Trim();
            draft.SupportsVision = visionCheck.IsChecked == true;
            draft.SupportsImageGeneration = imageCheck.IsChecked == true;
            draft.DefaultModelId = string.IsNullOrWhiteSpace(modelCombo.Text) ? null : modelCombo.Text.Trim();
            if (draft.Kind == ProviderKind.GoogleVertex)
            {
                draft.OAuthClientId = clientIdBox.Text.Trim();
                draft.ProjectId = projectBox.Text.Trim();
                draft.Location = string.IsNullOrWhiteSpace(regionBox.Text) ? "global" : regionBox.Text.Trim();
            }
        }

        // Builds a throwaway probe connection (id "__probe") seeded with the typed key, so probing
        // never overwrites the real connection's stored key or leaks on cancel.
        ProviderConnection ProbeConnection()
        {
            Snapshot();
            var clone = Clone(draft)!;
            clone.Id = ProbeId;
            var typed = keyBox.Password.Trim();
            if (!string.IsNullOrWhiteSpace(typed))
                secrets.Set(SecretKeys.ApiKeyFor(ProbeId), typed);
            else if (!string.IsNullOrWhiteSpace(draft.Id) && draft.Id != ProbeId)
            {
                // Editing without retyping the key — copy the stored one for the probe.
                var stored = secrets.Get(SecretKeys.ApiKeyFor(draft.Id));
                if (!string.IsNullOrWhiteSpace(stored)) secrets.Set(SecretKeys.ApiKeyFor(ProbeId), stored);
            }
            return clone;
        }

        fetchButton.Click += async (_, _) =>
        {
            statusText.Text = "Fetching models…";
            try
            {
                var models = await probe.ListModelsAsync(ProbeConnection());
                if (models.Count == 0)
                {
                    statusText.Text = "No models returned — you can type a model id.";
                }
                else
                {
                    var ids = models.Select(m => m.Id).ToList();
                    modelCombo.ItemsSource = ids;
                    if (string.IsNullOrWhiteSpace(modelCombo.Text)) modelCombo.Text = ids[0];
                    statusText.Text = $"Found {ids.Count} model(s).";
                }
            }
            catch (Exception ex) { statusText.Text = $"Couldn't fetch models: {ex.Message}"; }
            finally { secrets.Delete(SecretKeys.ApiKeyFor(ProbeId)); }
        };

        testButton.Click += async (_, _) =>
        {
            statusText.Text = "Testing…";
            try
            {
                var result = await probe.TestAsync(ProbeConnection());
                statusText.Text = result.Ok ? $"✓ {result.Message}" : $"✗ {result.Message}";
            }
            catch (Exception ex) { statusText.Text = $"✗ {ex.Message}"; }
            finally { secrets.Delete(SecretKeys.ApiKeyFor(ProbeId)); }
        };

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = isEdit ? "Edit provider" : "Add a provider",
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 460 },
            PrimaryButtonText = isEdit ? "Save" : "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            Snapshot();
            if (string.IsNullOrWhiteSpace(draft.DisplayName))
            {
                statusText.Text = "Please enter a name.";
                args.Cancel = true;
            }
            else if (draft.Kind != ProviderKind.GoogleVertex && string.IsNullOrWhiteSpace(draft.BaseUrl))
            {
                statusText.Text = "Please enter a base URL.";
                args.Cancel = true;
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        var apiKey = keyBox.Password.Trim();
        var clientSecret = clientSecretBox.Password.Trim();
        return new ProviderEditResult(
            draft,
            string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            string.IsNullOrWhiteSpace(clientSecret) ? null : clientSecret);
    }

    private static ProviderConnection? Clone(ProviderConnection? c) => c is null ? null : new ProviderConnection
    {
        Id = c.Id,
        DisplayName = c.DisplayName,
        Kind = c.Kind,
        BaseUrl = c.BaseUrl,
        Enabled = c.Enabled,
        SupportsVision = c.SupportsVision,
        SupportsImageGeneration = c.SupportsImageGeneration,
        Models = c.Models.Select(m => new ProviderModel(m.Id, m.DisplayName)).ToList(),
        DefaultModelId = c.DefaultModelId,
        ProjectId = c.ProjectId,
        Location = c.Location,
        OAuthClientId = c.OAuthClientId,
        KeepAlive = c.KeepAlive,
    };

    private static bool IsLocalEndpoint(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
}
