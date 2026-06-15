using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAI.Core.Providers;

namespace WindowsAI_App.Controls;

/// <summary>
/// Builds the app's modal dialogs in code so every caller gets the same professional, on-brand
/// <see cref="ContentDialog"/> (the implicit ContentDialog style in App.xaml supplies the glass
/// surface) without repeating boilerplate. Each call needs the owning element's
/// <see cref="XamlRoot"/> — required for dialogs in a desktop WinUI app — and only one dialog may
/// be open at a time, which matches these user-initiated, one-at-a-time flows.
/// </summary>
public static class DialogService
{
    /// <summary>A yes/no confirmation. Returns true only if the user chose the primary action.</summary>
    public static async Task<bool> ConfirmAsync(
        XamlRoot root, string title, string message,
        string confirmText = "OK", bool isDestructive = false)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = "Cancel",
            // For destructive actions default to Cancel so a stray Enter doesn't delete.
            DefaultButton = isDestructive ? ContentDialogButton.Close : ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    /// <summary>A single-line text prompt. Returns the trimmed text, or null if cancelled or left blank.</summary>
    public static async Task<string?> PromptTextAsync(
        XamlRoot root, string title, string label, string initialText = "",
        string placeholder = "", string primaryText = "Save")
    {
        var box = new TextBox
        {
            Text = initialText,
            PlaceholderText = placeholder,
            SelectionStart = initialText.Length,
        };
        if (await ShowInputAsync(root, title, label, box, primaryText) != ContentDialogResult.Primary)
            return null;

        var text = box.Text.Trim();
        return text.Length == 0 ? null : text;
    }

    /// <summary>A masked secret prompt (API keys). Returns the entered secret, or null if cancelled or blank.</summary>
    public static async Task<string?> PromptPasswordAsync(
        XamlRoot root, string title, string description,
        string placeholder = "", string primaryText = "Save")
    {
        var box = new PasswordBox { PlaceholderText = placeholder };
        if (await ShowInputAsync(root, title, description, box, primaryText) != ContentDialogResult.Primary)
            return null;

        var secret = box.Password.Trim();
        return secret.Length == 0 ? null : secret;
    }

    /// <summary>The settings chosen in the conversation-settings dialog.</summary>
    public sealed record ConversationSettingsResult(string SystemPrompt, ModelOption? Model);

    /// <summary>
    /// Per-conversation settings: a multiline system-prompt box plus a model picker. Returns the
    /// chosen values, or null if cancelled. An empty system prompt means "use the app default".
    /// </summary>
    public static async Task<ConversationSettingsResult?> PromptConversationSettingsAsync(
        XamlRoot root, string systemPrompt, IReadOnlyList<ModelOption> models, ModelOption? selected)
    {
        var promptBox = new TextBox
        {
            Text = systemPrompt,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            Height = 130,
            PlaceholderText = "Leave blank to use the default assistant prompt.",
        };
        ScrollViewer.SetVerticalScrollBarVisibility(promptBox, ScrollBarVisibility.Auto);

        var modelCombo = new ComboBox
        {
            ItemsSource = models,
            DisplayMemberPath = nameof(ModelOption.DisplayName),
            SelectedItem = selected,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "System prompt", Opacity = 0.7 });
        panel.Children.Add(promptBox);
        panel.Children.Add(new TextBlock { Text = "Model", Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0) });
        panel.Children.Add(modelCombo);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "Conversation settings",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        promptBox.Loaded += (_, _) => promptBox.Focus(FocusState.Programmatic);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return null;

        return new ConversationSettingsResult(promptBox.Text.Trim(), modelCombo.SelectedItem as ModelOption);
    }

    /// <summary>Shows a labelled single-input dialog and focuses the input on open.</summary>
    private static Task<ContentDialogResult> ShowInputAsync(
        XamlRoot root, string title, string label, Control input, string primaryText)
    {
        var panel = new StackPanel { Spacing = 10 };
        if (!string.IsNullOrEmpty(label))
            panel.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
        panel.Children.Add(input);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = title,
            Content = panel,
            PrimaryButtonText = primaryText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };

        input.Loaded += (_, _) => input.Focus(FocusState.Programmatic);
        return dialog.ShowAsync().AsTask();
    }
}
