using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WindowsAI.Core;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;
using WindowsAI.Services.Hotkeys;
using WindowsAI.Services.Inference;
using WindowsAI.Services.Security;
using WindowsAI.Services.Tray;
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

    /// <summary>Application-wide service provider.</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    public App()
    {
        InitializeComponent();
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Platform-agnostic core (chat, providers, settings).
        services.AddWindowsAiCore();

        // Windows-specific services.
        services.AddSingleton<ISecretStore, DpapiSecretStore>();

        // Local RAG: keyword retrieval over the Windows Search index, then semantic
        // re-ranking via local embeddings (falls back to keyword order if unavailable).
        services.AddSingleton<WindowsAI.Services.Search.WindowsSearchRetriever>();
        services.AddSingleton<WindowsAI.Core.Context.ILocalContextRetriever>(sp =>
            new WindowsAI.Core.Context.RerankingContextRetriever(
                sp.GetRequiredService<WindowsAI.Services.Search.WindowsSearchRetriever>(),
                sp.GetRequiredService<WindowsAI.Core.Context.IEmbeddingClient>()));

        // Overlay launcher search: apps + file names + settings, fanned out per keystroke.
        services.AddSingleton<WindowsAI.Services.Search.AppCatalog>();
        services.AddSingleton<WindowsAI.Services.Search.FileNameSearch>();
        services.AddSingleton<WindowsAI.Services.Search.OverlaySearchService>();
        services.AddSingleton<WindowsAI.Core.Search.OverlayRecentsStore>();

        // View models. ChatViewModel is a singleton so the active model and conversation
        // stay in sync across the overlay, the chat page, and the models page.
        services.AddSingleton<ChatViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<HistoryViewModel>();

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

        // Create the overlay once and keep it alive (hidden) for the whole session so the
        // global hotkey and tray icon remain available. It is never Activated here — the app
        // lives in the tray until summoned.
        _overlay = new OverlayWindow();

        // Bind the global summon hotkey to the overlay's HWND. Registration can fail
        // transiently (e.g. a previous instance not fully exited still holds the chord),
        // so retry instead of failing silently forever.
        _hotkey = new HotkeyManager(_overlay.Hwnd);
        _hotkey.Pressed += () => _overlay?.ToggleOverlay();
        _ = RegisterHotkeyWithRetryAsync();

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

    /// <summary>Keeps retrying hotkey registration until the chord is free (or we give up).</summary>
    private async Task RegisterHotkeyWithRetryAsync()
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            if (_hotkey is null || _hotkey.Register(HotkeyDefinition.Default))
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
