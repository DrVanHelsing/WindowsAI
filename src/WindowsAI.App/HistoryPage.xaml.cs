using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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

    private async void DeleteConfirmed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
            await ViewModel.DeleteAsync(id);
    }

    // Hover-reveal: the delete button is dimmed until its row is pointed at.
    private static void SetDeleteOpacity(object templateRoot, double opacity)
    {
        if (templateRoot is FrameworkElement root &&
            root.FindName("DeleteButton") is FrameworkElement button)
            button.Opacity = opacity;
    }

    private void Row_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => SetDeleteOpacity(sender, 1);

    private void Row_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        => SetDeleteOpacity(sender, 0.35);
}
