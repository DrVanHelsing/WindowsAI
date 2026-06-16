using System.Diagnostics;
using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Pickers;
using Windows.UI;
using Windows.UI.ViewManagement;
using WindowsAI_App.Controls;
using WindowsAI_App.ViewModels;

namespace WindowsAI_App;

/// <summary>Settings, organized by feature: appearance, per-capability models, feature toggles, web search, PC indexing.</summary>
public sealed partial class SettingsPage : Page
{
    // Curated accent palette. null = follow the Windows accent color.
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
    private readonly StackPanel[] _sections;

    public SettingsViewModel ViewModel { get; }
    public PcIndexingViewModel Pc { get; }

    public string AppVersion { get; } =
        typeof(SettingsPage).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public SettingsPage()
    {
        ViewModel = App.GetService<SettingsViewModel>();
        Pc = App.GetService<PcIndexingViewModel>();
        InitializeComponent();

        _sections =
        [
            AppearanceSection, ChatSection, VisionSection, WritingSection,
            WebSearchSection, ImageSection, PcIndexingSection, PrivacySection, AboutSection,
        ];

        BuildAccentSwatches();
        SectionRail.SelectedIndex = 0;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
        await Pc.RefreshStatusAsync();
    }

    private void SectionRail_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppearanceSection is null)
            return;

        var index = SectionRail.SelectedIndex;
        for (var i = 0; i < _sections.Length; i++)
            _sections[i].Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Accent swatches ──────────────────────────────────────────────────────

    private void BuildAccentSwatches()
    {
        var osAccent = new UISettings().GetColorValue(UIColorType.Accent);

        foreach (var (name, hex) in AccentPresets)
        {
            var color = hex is null ? osAccent : ParseHex(hex);
            var ring = new Ellipse
            {
                Width = 32,
                Height = 32,
                StrokeThickness = 2,
                Stroke = (Brush)Application.Current.Resources["TextPrimaryBrush"],
                Opacity = 0,
            };
            var dot = new Ellipse { Width = 24, Height = 24, Fill = new SolidColorBrush(color) };
            var content = new Grid();
            content.Children.Add(dot);
            if (hex is null)
            {
                content.Children.Add(new FontIcon
                {
                    FontSize = 11,
                    Glyph = "",
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
            AutomationProperties.SetName(button, name);
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

    // ── Web search key ────────────────────────────────────────────────────────

    private async void ManageWebSearchKey_Click(object sender, RoutedEventArgs e)
    {
        var key = await DialogService.PromptPasswordAsync(
            XamlRoot, "Tavily API key",
            "Your Tavily Search key from tavily.com (free tier available). Stored encrypted (DPAPI) on this PC only.",
            placeholder: "tvly-…");
        if (key is not null)
            ViewModel.SaveWebSearchKey(key);
    }

    private void RemoveWebSearchKey_Click(object sender, RoutedEventArgs e)
        => ViewModel.ClearWebSearchKey();

    // ── Hotkeys ─────────────────────────────────────────────────────────────────

    private async void ChangeSummonHotkey_Click(object sender, RoutedEventArgs e)
    {
        var chord = await DialogService.PromptHotkeyAsync(XamlRoot, "Summon shortcut", ViewModel.SummonHotkey);
        if (chord is not null)
        {
            await ViewModel.SetSummonHotkeyAsync(chord);
            await NoteRestartAsync();
        }
    }

    private async void ChangeWritingHotkey_Click(object sender, RoutedEventArgs e)
    {
        var chord = await DialogService.PromptHotkeyAsync(XamlRoot, "Writing Tools shortcut", ViewModel.WritingHotkey);
        if (chord is not null)
        {
            await ViewModel.SetWritingHotkeyAsync(chord);
            await NoteRestartAsync();
        }
    }

    private async void ChangeVisionHotkey_Click(object sender, RoutedEventArgs e)
    {
        var chord = await DialogService.PromptHotkeyAsync(XamlRoot, "Visual Intelligence shortcut", ViewModel.VisionHotkey);
        if (chord is not null)
        {
            await ViewModel.SetVisionHotkeyAsync(chord);
            await NoteRestartAsync();
        }
    }

    private Task NoteRestartAsync() => DialogService.ConfirmAsync(
        XamlRoot, "Shortcut saved", "The new shortcut takes effect the next time WindowsAI starts.", confirmText: "OK");

    // ── PC Indexing ─────────────────────────────────────────────────────────────

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        var hwnd = (Application.Current as App)?.AppShell?.Hwnd ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero)
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
            await Pc.AddFolderAsync(folder.Path);
    }

    private async void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is string path)
            await Pc.RemoveFolderAsync(path);
    }

    private async void RefreshIndex_Click(object sender, RoutedEventArgs e)
        => await Pc.RefreshStatusAsync();

    private void OpenIndexingOptions_Click(object sender, RoutedEventArgs e)
        => Pc.OpenIndexingOptions();

    // ── Privacy & data ────────────────────────────────────────────────────────

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsAI");
        TryStart(dir);
    }

    private void OpenCrashLog_Click(object sender, RoutedEventArgs e)
    {
        var log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WindowsAI-crash.log");
        TryStart(System.IO.File.Exists(log) ? log : System.IO.Path.GetTempPath());
    }

    private static void TryStart(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { /* best effort */ }
    }
}
