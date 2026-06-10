using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAI_App.ViewModels;

namespace WindowsAI_App;

/// <summary>
/// Model management: lists every available model (local and cloud) and lets the user pick
/// the active one. Bound to the shared <see cref="ChatViewModel"/> so the choice is reflected
/// immediately in the chat surface and the overlay.
/// </summary>
public sealed partial class ModelsPage : Page
{
    public ChatViewModel ViewModel { get; }

    public ModelsPage()
    {
        ViewModel = App.GetService<ChatViewModel>();
        InitializeComponent();

        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.InitializeAsync();
}
