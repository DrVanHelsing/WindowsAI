using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using WindowsAI_App.Controls;
using WindowsAI_App.ViewModels;

namespace WindowsAI_App;

/// <summary>
/// Conversation history: lists saved sessions, opens one into the chat surface, or deletes it.
/// </summary>
public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    public HistoryPage()
    {
        ViewModel = App.GetService<HistoryViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.RefreshAsync();
    }

    private async void Conversation_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ConversationSummary summary)
            return;

        await ViewModel.OpenAsync(summary.Id);
        ((App)Application.Current).AppShell?.ShowChat();
    }

    private async void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConversationSummary summary })
            return;

        var newTitle = await DialogService.PromptTextAsync(
            XamlRoot,
            "Rename conversation",
            "Give this conversation a name.",
            initialText: summary.Title,
            placeholder: "Conversation name");

        if (newTitle is not null && newTitle != summary.Title)
            await ViewModel.RenameAsync(summary.Id, newTitle);
    }

    private async void Pin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConversationSummary summary })
            return;

        await ViewModel.PinAsync(summary.Id, !summary.IsPinned);
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ConversationSummary summary })
            return;

        var confirmed = await DialogService.ConfirmAsync(
            XamlRoot,
            "Delete conversation?",
            $"“{summary.Title}” will be permanently deleted. This can’t be undone.",
            confirmText: "Delete",
            isDestructive: true);

        if (confirmed)
            await ViewModel.DeleteAsync(summary.Id);
    }

    // Hover-reveal: the row's action buttons are dimmed until the row is pointed at.
    private static void SetActionsOpacity(object templateRoot, double opacity)
    {
        if (templateRoot is FrameworkElement root &&
            root.FindName("RowActions") is FrameworkElement actions)
            actions.Opacity = opacity;
    }

    private void Row_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => SetActionsOpacity(sender, 1);

    private void Row_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => SetActionsOpacity(sender, 0.35);
}
