using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace WindowsAI_App.Controls;

/// <summary>
/// Renders a message with a hosted WebView2 (markdown-it + KaTeX + highlight.js) for the cases the
/// native <see cref="MessageContentControl"/> can't typeset — real LaTeX math and complex tables.
/// Assets are vendored under <c>Assets/richview/</c> and served from a local virtual host (no network).
/// The page reports its content height back so the control sizes itself inside the scrolling chat list.
/// </summary>
/// <remarks>
/// Used only for final (non-streaming) text via <see cref="MessageContentControl"/>'s adaptive swap;
/// the background stays transparent so the liquid glass shows through.
/// </remarks>
public sealed partial class RichMessageView : ContentControl
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown), typeof(string), typeof(RichMessageView),
        new PropertyMetadata(string.Empty, static (d, _) => ((RichMessageView)d).Push()));

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    private readonly WebView2 _web = new();
    private bool _ready;

    public RichMessageView()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        _web.DefaultBackgroundColor = Microsoft.UI.Colors.Transparent;
        Content = _web;

        Loaded += OnLoaded;
        ActualThemeChanged += (_, _) => Push();
    }

    /// <summary>Builds the shared CoreWebView2 environment up front so the first bubble renders fast.</summary>
    public static void PrewarmEnvironment() => _ = EnvironmentAsync();

    private static Task<CoreWebView2Environment>? s_environment;
    private static Task<CoreWebView2Environment> EnvironmentAsync()
        => s_environment ??= CoreWebView2Environment.CreateWithOptionsAsync(null, null, null).AsTask();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_web.CoreWebView2 is not null)
            return;

        try
        {
            await _web.EnsureCoreWebView2Async(await EnvironmentAsync());
        }
        catch
        {
            return; // WebView2 runtime missing — caller keeps the native fallback visible.
        }

        var core = _web.CoreWebView2;
        if (core is null)
            return;

        var settings = core.Settings;
        settings.AreDefaultContextMenusEnabled = true;
        settings.IsZoomControlEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsStatusBarEnabled = false;
#if !DEBUG
        settings.AreDevToolsEnabled = false;
#endif

        var assets = Path.Combine(AppContext.BaseDirectory, "Assets", "richview");
        core.SetVirtualHostNameToFolderMapping("richview.local", assets, CoreWebView2HostResourceAccessKind.DenyCors);

        core.WebMessageReceived += OnWebMessage;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;

        core.Navigate("https://richview.local/index.html");
    }

    private void OnWebMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch { return; }

        if (message == "ready")
        {
            _ready = true;
            Push();
            return;
        }

        if (int.TryParse(message, out var height) && height > 0)
            _web.Height = height;
    }

    private void Push()
    {
        if (!_ready || _web.CoreWebView2 is null)
            return;

        var payload = JsonSerializer.Serialize(new
        {
            md = Markdown ?? string.Empty,
            dark = ActualTheme != ElementTheme.Light,
        });
        _web.CoreWebView2.PostWebMessageAsString(payload);
    }

    // Links navigate out to the default browser rather than inside the bubble.
    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith("https://richview.local/", StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;
        LaunchExternal(e.Uri);
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        LaunchExternal(e.Uri);
    }

    private static async void LaunchExternal(string uri)
    {
        try
        {
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
                await global::Windows.System.Launcher.LaunchUriAsync(parsed);
        }
        catch { /* best effort */ }
    }
}
