using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.ViewManagement;
using WinRT.Interop;
using WindowsAI_App.Controls;
using WindowsAI_App.ViewModels;
using ISecretStore = WindowsAI.Core.Security.ISecretStore;
using ISettingsStore = WindowsAI.Core.Configuration.ISettingsStore;

namespace WindowsAI_App.Windows;

/// <summary>
/// First-run setup wizard: welcome → connect a provider (cloud key, Google sign-in, or local — all
/// optional and equal) → choose features → PC indexing → personalize → done. Skippable at any step;
/// everything it sets is editable later in Settings. Shown once, gated by <c>Onboarding.HasCompleted</c>.
/// Bindings are wired in code-behind (the 1.8 markup compiler fails silently on x:Bind in Windows).
/// </summary>
public sealed partial class OnboardingWindow : Window
{
    private static readonly (string Name, string? Hex)[] AccentPresets =
    [
        ("Windows default", null), ("Electric Iris", "#6E7BFF"), ("Indigo", "#6366F1"),
        ("Blue", "#3B82F6"), ("Cyan", "#06B6D4"), ("Teal", "#14B8A6"),
        ("Green", "#22C55E"), ("Amber", "#F59E0B"), ("Violet", "#8B5CF6"),
    ];

    private readonly Action? _onFinish;
    private readonly ISettingsStore _settings;
    private StackPanel[] _steps = [];
    private int _step;
    private bool _initializing;

    public SettingsViewModel Settings { get; }
    public ProvidersViewModel Providers { get; }
    public PcIndexingViewModel Pc { get; }

    public FrameworkElement ThemeRoot => RootGrid;
    public IntPtr Hwnd => WindowNative.GetWindowHandle(this);

    public OnboardingWindow(Action? onFinish = null)
    {
        _onFinish = onFinish;
        Settings = App.GetService<SettingsViewModel>();
        Providers = App.GetService<ProvidersViewModel>();
        Pc = App.GetService<PcIndexingViewModel>();
        _settings = App.GetService<ISettingsStore>();

        InitializeComponent();
        _steps = [StepWelcome, StepConnect, StepFeatures, StepIndex, StepPersonalize, StepDone];

        // Set on the root in code-behind: {StaticResource} on a Window's root element silently
        // breaks the 1.8 markup compiler. (Inter is applied globally via App.xaml's implicit
        // TextBlock style, so only the background needs setting here.)
        RootGrid.Background = (Brush)Application.Current.Resources["DialogSurfaceBrush"];

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = "WindowsAI setup";
        try { SystemBackdrop = new MicaBackdrop(); } catch { /* fall back to the solid background */ }

        var appWindow = AppWindow;
        appWindow.Resize(new SizeInt32(780, 700));
        CenterOnScreen(appWindow);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }

        ConnectProviderList.ItemsSource = Providers.Providers;
        FoldersList.ItemsSource = Pc.Folders;

        _initializing = true;
        VisionToggle.IsOn = Settings.VisualIntelligenceEnabled;
        WritingToggle.IsOn = Settings.WritingToolsEnabled;
        WebSearchToggle.IsOn = Settings.WebSearchEnabled;
        ImageToggle.IsOn = Settings.ImageGenerationEnabled;
        PcToggle.IsOn = Pc.Enabled;
        ThemeRadios.SelectedIndex = Settings.ThemeIndex;
        _initializing = false;

        // Events are wired here (not in XAML) — the 1.8 markup compiler fails silently on
        // event hookups for some controls in Window XAML.
        VisionToggle.Toggled += FeatureToggle_Toggled;
        WritingToggle.Toggled += FeatureToggle_Toggled;
        WebSearchToggle.Toggled += FeatureToggle_Toggled;
        ImageToggle.Toggled += FeatureToggle_Toggled;
        PcToggle.Toggled += FeatureToggle_Toggled;
        ThemeRadios.SelectionChanged += ThemeRadios_SelectionChanged;

        BuildAccentSwatches();
        ShowStep(0);
    }

    private static void CenterOnScreen(AppWindow appWindow)
    {
        var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        appWindow.Move(new PointInt32(
            area.X + (area.Width - appWindow.Size.Width) / 2,
            area.Y + (area.Height - appWindow.Size.Height) / 2));
    }

    // ── Step machine ────────────────────────────────────────────────────────────

    private async void ShowStep(int index)
    {
        _step = index;
        for (var i = 0; i < _steps.Length; i++)
            _steps[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;

        if (index == 1)
            ConnectEmptyHint.Visibility = Providers.HasProviders ? Visibility.Collapsed : Visibility.Visible;

        BackButton.Visibility = index == 0 ? Visibility.Collapsed : Visibility.Visible;
        var isLast = index == _steps.Length - 1;
        NextButton.Content = isLast ? "Start using WindowsAI" : "Next";
        SkipButton.Visibility = isLast ? Visibility.Collapsed : Visibility.Visible;

        var total = Pc.Enabled ? 6 : 5;
        StepCounter.Text = $"Step {Math.Min(index + 1, total)} of {total}";

        if (index == 3)
            await RefreshIndexUiAsync();
    }

    private int NextIndex(int from)
    {
        var next = from + 1;
        if (next == 3 && !Pc.Enabled) next = 4; // skip PC indexing step when disabled
        return Math.Min(next, _steps.Length - 1);
    }

    private int PrevIndex(int from)
    {
        var prev = from - 1;
        if (prev == 3 && !Pc.Enabled) prev = 2;
        return Math.Max(prev, 0);
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == _steps.Length - 1)
            Finish();
        else
            ShowStep(NextIndex(_step));
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(PrevIndex(_step));

    private void Skip_Click(object sender, RoutedEventArgs e) => Finish();

    private async void Finish()
    {
        _settings.Current.Onboarding.HasCompleted = true;
        _settings.Current.Onboarding.Version = typeof(OnboardingWindow).Assembly.GetName().Version?.ToString(3);
        await _settings.SaveAsync();
        _onFinish?.Invoke();
        Close();
    }

    // ── Feature toggles + theme ──────────────────────────────────────────────────

    private void FeatureToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_initializing)
            return;
        Settings.VisualIntelligenceEnabled = VisionToggle.IsOn;
        Settings.WritingToolsEnabled = WritingToggle.IsOn;
        Settings.WebSearchEnabled = WebSearchToggle.IsOn;
        Settings.ImageGenerationEnabled = ImageToggle.IsOn;
        Pc.Enabled = PcToggle.IsOn;
    }

    private void ThemeRadios_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ThemeRadios.SelectedIndex < 0)
            return;
        Settings.ThemeIndex = ThemeRadios.SelectedIndex;
    }

    // ── Connect step ─────────────────────────────────────────────────────────────

    private async void AddProvider_Click(object sender, RoutedEventArgs e)
    {
        var result = await ProviderEditDialog.ShowAsync(RootGrid.XamlRoot, Providers.Probe, App.GetService<ISecretStore>(), existing: null);
        if (result is null)
            return;

        var saved = await Providers.AddAsync(result.Connection, result.ApiKey);
        if (result.ClientSecret is not null)
            await Providers.SetClientSecretAsync(saved, result.ClientSecret);

        ConnectEmptyHint.Visibility = Providers.HasProviders ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── PC indexing step ─────────────────────────────────────────────────────────

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        InitializeWithWindow.Initialize(picker, Hwnd);
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            await Pc.AddFolderAsync(folder.Path);
            await RefreshIndexUiAsync();
        }
    }

    private async Task RefreshIndexUiAsync()
    {
        await Pc.RefreshStatusAsync();
        IndexProgress.Value = Pc.ProgressPercent;
        IndexStatusText.Text = Pc.StatusText;
    }

    // ── Accent swatches ──────────────────────────────────────────────────────────

    private void BuildAccentSwatches()
    {
        var osAccent = new UISettings().GetColorValue(UIColorType.Accent);
        foreach (var (name, hex) in AccentPresets)
        {
            var color = hex is null ? osAccent : ParseHex(hex);
            var dot = new Ellipse { Width = 26, Height = 26, Fill = new SolidColorBrush(color) };
            var button = new Button
            {
                Width = 38,
                Height = 38,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(19),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = dot,
                Tag = hex,
            };
            ToolTipService.SetToolTip(button, name);
            button.Click += async (s, _) => await Settings.SetAccentAsync(((FrameworkElement)s).Tag as string);
            AccentSwatchPanel.Children.Add(button);
        }
    }

    private static Color ParseHex(string hex)
    {
        var s = hex.TrimStart('#');
        var v = uint.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromArgb(0xFF, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }
}
