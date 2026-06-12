using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace WindowsAI_App;

/// <summary>
/// The full application window: a custom sidebar + content frame shell.
/// BrandBar is designated as the drag region so the window can be moved by
/// dragging the top of the sidebar; caption buttons float over the content column.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>The live acrylic backdrop handle, so ThemeSyncService can flip its tint with the OS theme.</summary>
    public Helpers.GlassBackdropHandle Backdrop { get; }

    /// <summary>Root element whose RequestedTheme ThemeSyncService flips to drive built-in control styles.</summary>
    public FrameworkElement ThemeRoot => RootGrid;

    public MainWindow()
    {
        InitializeComponent();

        // Slightly heavier tint than the overlay so long-form chat stays readable.
        Backdrop = Helpers.GlassBackdrop.Apply(this, tintOpacity: 0.55f, luminosityOpacity: 0.70f);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(BrandBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        ContentFrame.Navigate(typeof(MainPage), null, new EntranceNavigationTransitionInfo());
        NavChat.IsChecked = true;
    }

    /// <summary>Re-selects Chat and navigates the frame to it.</summary>
    public void ShowChat()
    {
        NavChat.IsChecked = true;
        if (ContentFrame.CurrentSourcePageType != typeof(MainPage))
            ContentFrame.Navigate(typeof(MainPage), null, new SlideNavigationTransitionInfo { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    private void NavItem_Checked(object sender, RoutedEventArgs e)
    {
        if (ContentFrame is null) return; // fired during InitializeComponent

        var tag = ((RadioButton)sender).Tag as string;
        var target = tag switch
        {
            "history"  => typeof(HistoryPage),
            "models"   => typeof(ModelsPage),
            "settings" => typeof(SettingsPage),
            _          => typeof(MainPage),
        };

        if (ContentFrame.CurrentSourcePageType != target)
            ContentFrame.Navigate(target, null, new SlideNavigationTransitionInfo
            {
                Effect = SlideNavigationTransitionEffect.FromRight
            });
    }

    private void NewChatButton_Click(object sender, RoutedEventArgs e)
    {
        NavChat.IsChecked = true;

        // Navigate to Chat and ask MainPage to start a fresh conversation.
        ContentFrame.Navigate(typeof(MainPage), "new", new EntranceNavigationTransitionInfo());
    }
}
