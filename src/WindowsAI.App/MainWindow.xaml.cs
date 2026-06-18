using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace WindowsAI_App;

/// <summary>
/// The full application window: a full-width title strip over a sidebar + content
/// frame shell. AppTitleBar spans the whole top as the drag region, so the window
/// is grabbable across its full width; caption buttons sit at its right.
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>The live acrylic backdrop handle, so ThemeSyncService can flip its tint with the OS theme.</summary>
    public Helpers.GlassBackdropHandle Backdrop { get; }

    /// <summary>Root element whose RequestedTheme ThemeSyncService flips to drive built-in control styles.</summary>
    public FrameworkElement ThemeRoot => RootGrid;

    /// <summary>Native handle — lets ThemeSyncService tint the window border to the OS accent.</summary>
    public IntPtr Hwnd { get; }

    private readonly AppWindow _appWindow;

    /// <summary>True only during app shutdown, so the close box hides-to-tray but Quit really closes.</summary>
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();

        Hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow;

        // The app lives in the tray: closing the window hides it (keeping the chat session intact)
        // rather than destroying it, so re-opening from the tray restores this same window instead
        // of stacking a fresh one on top. Real teardown happens via the tray's Quit (see AllowClose).
        _appWindow.Closing += OnClosing;

        // Slightly heavier tint than the overlay so long-form chat stays readable.
        Backdrop = Helpers.GlassBackdrop.Apply(this, tintOpacity: 0.55f, luminosityOpacity: 0.70f);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        ContentFrame.Navigate(typeof(MainPage), null, new EntranceNavigationTransitionInfo());
        NavChat.IsChecked = true;
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
            return;

        args.Cancel = true;
        _appWindow.Hide();
    }

    /// <summary>Shows the window and restores it from a minimized/hidden state, bringing it forward.</summary>
    public void RestoreAndActivate()
    {
        if (_appWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();

        _appWindow.Show();
        Activate();
    }

    /// <summary>Lets the next close actually destroy the window. Called on app quit.</summary>
    public void AllowClose() => _allowClose = true;

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
