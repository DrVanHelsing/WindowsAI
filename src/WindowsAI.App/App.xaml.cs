using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WindowsAI.Core;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;
using WindowsAI.Services.Hotkeys;
using WindowsAI.Services.Inference;
using WindowsAI.Services.Security;
using WindowsAI.Services.Tray;
using WindowsAI_App.Services;
using WindowsAI_App.ViewModels;
using WindowsAI_App.Windows;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace WindowsAI_App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// Owns the dependency-injection container and the always-alive overlay + global hotkey.
/// The app starts to the tray: no top-level window is shown until the user summons the
/// overlay (Alt+Space) or opens the full app from the tray menu.
/// </summary>
public partial class App : Application
{
    private OverlayWindow? _overlay;
    private HotkeyManager? _hotkey;
    private NativeTrayIcon? _tray;
    private MainWindow? _mainWindow;
    private ThemeSyncService? _themeSync;
    private WritingToolsController? _writingTools;
    private VisualIntelligenceController? _visualIntelligence;

    /// <summary>Application-wide service provider.</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    /// <summary>Crash log path: %TEMP%\WindowsAI-crash.log. Captures otherwise-silent fatal exceptions.</summary>
    private static readonly string CrashLogPath = Path.Combine(Path.GetTempPath(), "WindowsAI-crash.log");

    public App()
    {
        InitializeComponent();
        WireCrashLogging();
        Services = ConfigureServices();
    }

    /// <summary>
    /// Routes the three fatal-exception channels (XAML UI thread, AppDomain, unobserved tasks) to a
    /// log file so crashes that WER only records as a native stowed exception (no managed stack) are
    /// diagnosable. We deliberately do NOT swallow the XAML exception (<c>e.Handled</c> stays false):
    /// an unhandled exception means unknown state, and masking it risks corrupting the user's data —
    /// far better to fail fast, capture the stack, and fix the cause (as we did for the backdrop
    /// theme crash). Before a public release this file log should be superseded by a real crash
    /// reporter (e.g. Sentry / App Center) that uploads with user consent.
    /// </summary>
    private void WireCrashLogging()
    {
        UnhandledException += (_, e) => Log("Xaml", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("Task", e.Exception);
            e.SetObserved();
        };
    }

    private static void Log(string source, Exception? ex)
    {
        try
        {
            File.AppendAllText(CrashLogPath, $"\n===== {DateTime.Now:O} [{source}] =====\n{ex}\n");
        }
        catch
        {
            // Logging must never itself throw on the way down.
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Platform-agnostic core (chat, providers, settings).
        services.AddWindowsAiCore();

        // Windows-specific services.
        services.AddSingleton<ISecretStore, DpapiSecretStore>();

        // Type-anywhere writing tools: synthesizes the copy/paste chords to read and replace
        // the selection in whatever app has focus.
        services.AddSingleton<WindowsAI.Services.Writing.TextSelectionService>();

        // Visual intelligence: grabs a screen region (GDI) to ask a vision model about.
        services.AddSingleton<WindowsAI.Services.Capture.ScreenCaptureService>();

        // Local RAG: keyword retrieval over the Windows Search index, then semantic
        // re-ranking via local embeddings (falls back to keyword order if unavailable).
        services.AddSingleton<WindowsAI.Services.Search.WindowsSearchRetriever>();
        services.AddSingleton<WindowsAI.Core.Context.ILocalContextRetriever>(sp =>
            new WindowsAI.Core.Context.RerankingContextRetriever(
                sp.GetRequiredService<WindowsAI.Services.Search.WindowsSearchRetriever>(),
                sp.GetRequiredService<WindowsAI.Core.Context.IEmbeddingClient>()));

        // PC Indexing: manage the Windows Search index scope (folders) + read its status.
        services.AddSingleton<WindowsAI.Services.Search.WindowsSearchIndexService>();

        // Overlay launcher search: apps + file names + settings, fanned out per keystroke.
        services.AddSingleton<WindowsAI.Services.Search.AppCatalog>();
        services.AddSingleton<WindowsAI.Services.Search.FileNameSearch>();
        services.AddSingleton<WindowsAI.Services.Search.OverlaySearchService>();
        services.AddSingleton<WindowsAI.Core.Search.OverlayRecentsStore>();

        // View models. ChatViewModel is a singleton so the active model and conversation
        // stay in sync across the overlay, the chat page, and the models page.
        services.AddSingleton<ChatViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<ProvidersViewModel>();
        services.AddTransient<PcIndexingViewModel>();
        services.AddTransient<HistoryViewModel>();

        // Keeps every window in step with the Windows light/dark + accent settings, live.
        services.AddSingleton<ThemeSyncService>();

        return services.BuildServiceProvider();
    }

    /// <summary>Resolves a required service from the app container.</summary>
    public static T GetService<T>() where T : class => Services.GetRequiredService<T>();

    /// <summary>
    /// Builds a fresh, independent <see cref="ChatViewModel"/> (DI-resolved dependencies) rather
    /// than the shared shell singleton. Used by the overlay so its conversation stays separate.
    /// </summary>
    public static ChatViewModel CreateChatViewModel()
        => ActivatorUtilities.CreateInstance<ChatViewModel>(Services);

    /// <summary>The full application window, if it has been opened. Null while running tray-only.</summary>
    public MainWindow? AppShell => _mainWindow;

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Single instance: a second launch redirects its activation to the running
        // instance (which surfaces the UI) and exits. Without this, the duplicate
        // could never own the Alt+Space hotkey and the tray would show twice.
        var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("WindowsAI-Main");
        if (!mainInstance.IsCurrent)
        {
            await mainInstance.RedirectActivationToAsync(
                Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs());
            Environment.Exit(0);
            return;
        }
        mainInstance.Activated += OnActivationRedirected;

        var settingsStore = Services.GetRequiredService<ISettingsStore>();
        await settingsStore.LoadAsync();

        // Ensure Ollama is running before the first inference request. Fires in the
        // background so it doesn't delay the overlay appearing; any failure is silent.
        _ = OllamaLauncher.EnsureRunningAsync(settingsStore);

        // Build the shared WebView2 environment up front so the first math/table bubble
        // (RichMessageView) renders without a cold-start stall.
        Controls.RichMessageView.PrewarmEnvironment();

        // Create the overlay once and keep it alive (hidden) for the whole session so the
        // global hotkey and tray icon remain available. It is never Activated here — the app
        // lives in the tray until summoned.
        _overlay = new OverlayWindow();

        // Start theme/accent sync before the first window is shown so the overlay opens in the
        // correct theme, then bring the overlay under its control.
        _themeSync = Services.GetRequiredService<ThemeSyncService>();
        _themeSync.Start();
        _themeSync.RegisterWindow(_overlay.ThemeRoot);

        // Bind the global hotkeys to the overlay's HWND: Alt+Space summons the overlay,
        // Ctrl+Shift+Space opens the writing tools over the current selection, and Alt+Shift+S
        // starts a visual-intelligence region capture. Registration can fail transiently (e.g. a
        // previous instance not fully exited still holds a chord), so retry instead of failing
        // silently forever.
        // Hotkeys + the writing/visual controllers are gated by the user's feature flags and use
        // the (rebindable) chords from settings rather than the static defaults.
        var features = settingsStore.Current.Features;
        _hotkey = new HotkeyManager(_overlay.Hwnd);
        _hotkey.Add(ToDefinition(features.SummonHotkey), () => _overlay?.ToggleOverlay());

        if (features.WritingTools)
        {
            _writingTools = new WritingToolsController(
                Services.GetRequiredService<WindowsAI.Services.Writing.TextSelectionService>(),
                Services.GetRequiredService<WindowsAI.Core.Writing.WritingToolsService>(),
                _overlay.DispatcherQueue,
                window => _themeSync?.RegisterWindow(window.ThemeRoot));
            _hotkey.Add(ToDefinition(features.WritingToolsHotkey), () => _writingTools?.Trigger());
        }

        if (features.VisualIntelligence)
        {
            _visualIntelligence = new VisualIntelligenceController(
                Services.GetRequiredService<WindowsAI.Services.Capture.ScreenCaptureService>(),
                Services.GetRequiredService<WindowsAI.Core.Providers.VisionService>(),
                _overlay.DispatcherQueue,
                window => _themeSync?.RegisterWindow(window.ThemeRoot));
            _hotkey.Add(ToDefinition(features.VisualIntelligenceHotkey), () => _visualIntelligence?.Trigger());
        }

        _ = RegisterHotkeyWithRetryAsync();

        // First run: show the setup wizard over the tray app. It persists its own completion flag.
        if (!settingsStore.Current.Onboarding.HasCompleted)
        {
            var onboarding = new OnboardingWindow(ShowMainApp);
            _themeSync?.RegisterWindow(onboarding.ThemeRoot);
            onboarding.Activate();
        }

        // Native tray icon + menu. Built on Win32 (not H.NotifyIcon's XAML flyout) so it has
        // no dependency on an activated window's XamlRoot — the overlay is never activated.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        _tray = new NativeTrayIcon("WindowsAI — press Alt+Space", iconPath);
        _tray.AddMenuItem("Show search  (Alt+Space)", () => _overlay?.ShowOverlay());
        _tray.AddMenuItem("Open WindowsAI", ShowMainApp);
        _tray.AddSeparator();
        _tray.AddMenuItem("Quit", QuitApp);
        _tray.LeftClicked += () => _overlay?.ToggleOverlay();
        _tray.Show();
    }

    /// <summary>Maps a serializable settings chord to the native hotkey definition.</summary>
    private static HotkeyDefinition ToDefinition(WindowsAI.Core.Configuration.HotkeyChord chord)
        => new(chord.Modifiers, chord.VirtualKey);

    /// <summary>Keeps retrying hotkey registration until the chord is free (or we give up).</summary>
    private async Task RegisterHotkeyWithRetryAsync()
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            if (_hotkey is null || _hotkey.TryRegisterPending())
                return;
            await Task.Delay(1000);
        }
    }

    /// <summary>Another launch of the app was redirected here — surface the main window.</summary>
    private void OnActivationRedirected(object? sender, Microsoft.Windows.AppLifecycle.AppActivationArguments e)
        => _overlay?.DispatcherQueue.TryEnqueue(ShowMainApp);

    /// <summary>Summons the overlay search surface.</summary>
    public void ShowOverlay() => _overlay?.ShowOverlay();

    /// <summary>Toggles the overlay (same behavior as pressing the hotkey).</summary>
    public void ToggleOverlay() => _overlay?.ToggleOverlay();

    /// <summary>Opens (or re-focuses) the full application window.</summary>
    public void ShowMainApp()
    {
        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow();
            // Drop the reference when the window is closed so it can be reopened later.
            _mainWindow.Closed += (_, _) => _mainWindow = null;
            // Follow the OS theme/accent (flip the acrylic tint, tint the border) for the window's life.
            _themeSync?.RegisterWindow(_mainWindow.ThemeRoot, _mainWindow.Backdrop, _mainWindow.Hwnd);
        }

        _mainWindow.Activate();
    }

    /// <summary>Tears down the hotkey and tray and exits the process.</summary>
    public void QuitApp()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        _tray?.Dispose();
        _tray = null;
        Exit();
    }
}
