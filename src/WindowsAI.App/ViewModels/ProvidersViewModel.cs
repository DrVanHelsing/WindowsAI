using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WindowsAI.Core.Auth;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Providers;
using WindowsAI.Core.Security;

namespace WindowsAI_App.ViewModels;

/// <summary>
/// One provider connection as shown on a card in the Models &amp; providers hub: identity, endpoint,
/// live status, and enabled state. Display-only — edits go through <see cref="ProvidersViewModel"/>.
/// </summary>
public partial class ProviderConnectionViewModel : ObservableObject
{
    public ProviderConnectionViewModel(ProviderConnection model)
    {
        Model = model;
        Enabled = model.Enabled;
    }

    public ProviderConnection Model { get; }

    public string Id => Model.Id;
    public string DisplayName => Model.DisplayName;

    public string Endpoint => Model.Kind switch
    {
        ProviderKind.GoogleVertex => string.IsNullOrWhiteSpace(Model.ProjectId) ? "Vertex AI (Google sign-in)" : $"Vertex AI · {Model.ProjectId}",
        _ => Model.BaseUrl,
    };

    public string KindLabel => Model.Kind switch
    {
        ProviderKind.Ollama => "Local",
        ProviderKind.GoogleVertex => "Google sign-in",
        _ => "Cloud",
    };

    /// <summary>Segoe Fluent glyph: PC for local, account for Google, cloud otherwise.</summary>
    public string Glyph => Model.Kind switch
    {
        ProviderKind.Ollama => "",
        ProviderKind.GoogleVertex => "",
        _ => "",
    };

    public string DefaultModel => string.IsNullOrWhiteSpace(Model.DefaultModelId) ? "No default model" : Model.DefaultModelId!;

    /// <summary>True for Vertex connections — shows the Google "Sign in" action on the card.</summary>
    public bool IsGoogleSignIn => Model.Kind == ProviderKind.GoogleVertex;

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    /// <summary>"ok" | "warn" | "off" — drives the status dot colour + glyph.</summary>
    [ObservableProperty]
    public partial string StatusKind { get; set; } = "warn";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Not configured";

    /// <summary>The status dot colour derived from <see cref="StatusKind"/>.</summary>
    public Brush StatusBrush => new SolidColorBrush(StatusKind switch
    {
        "ok" => Color.FromArgb(0xFF, 0x22, 0xC5, 0x5E),   // green
        "off" => Color.FromArgb(0xFF, 0x6E, 0x72, 0x80),  // gray
        _ => Color.FromArgb(0xFF, 0xF5, 0x9E, 0x0B),       // amber
    });

    partial void OnStatusKindChanged(string value) => OnPropertyChanged(nameof(StatusBrush));
}

/// <summary>
/// Backs the providers hub: the editable list of <see cref="ProviderConnection"/>s plus the
/// operations the UI invokes — add (from a preset or custom), edit, remove, enable/disable, set the
/// API key, and test the connection. All changes flow through <see cref="ISettingsStore"/> +
/// <see cref="ISecretStore"/> and refresh the <see cref="IProviderRegistry"/>.
/// </summary>
public partial class ProvidersViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;
    private readonly IProviderRegistry _registry;
    private readonly ProviderProbe _probe;
    private readonly GoogleOAuthService _oauth;

    public ProvidersViewModel(
        ISettingsStore settings, ISecretStore secrets, IProviderRegistry registry,
        ProviderProbe probe, GoogleOAuthService oauth)
    {
        _settings = settings;
        _secrets = secrets;
        _registry = registry;
        _probe = probe;
        _oauth = oauth;
        Reload();
    }

    public ObservableCollection<ProviderConnectionViewModel> Providers { get; } = new();

    public bool HasProviders => Providers.Count > 0;

    public ProviderProbe Probe => _probe;
    public GoogleOAuthService OAuth => _oauth;

    /// <summary>Rebuilds the card list from settings and recomputes each provider's status.</summary>
    public void Reload()
    {
        Providers.Clear();
        foreach (var connection in _settings.Current.Providers)
        {
            var vm = new ProviderConnectionViewModel(connection);
            UpdateStatus(vm);
            Providers.Add(vm);
        }
        OnPropertyChanged(nameof(HasProviders));
    }

    /// <summary>Computes the status dot for a card from its kind + key/sign-in state.</summary>
    public void UpdateStatus(ProviderConnectionViewModel vm)
    {
        var c = vm.Model;
        if (!c.Enabled)
        {
            vm.StatusKind = "off";
            vm.StatusText = "Disabled";
            return;
        }

        bool configured = c.Kind switch
        {
            ProviderKind.Ollama => true,
            ProviderKind.GoogleVertex => _oauth.IsSignedIn(c) && !string.IsNullOrWhiteSpace(c.ProjectId),
            _ => IsLocalEndpoint(c.BaseUrl) || !string.IsNullOrWhiteSpace(_secrets.Get(SecretKeys.ApiKeyFor(c.Id))),
        };

        vm.StatusKind = configured ? "ok" : "warn";
        vm.StatusText = configured
            ? (c.Kind == ProviderKind.Ollama ? "Local · ready" : "Connected")
            : (c.Kind == ProviderKind.GoogleVertex ? "Sign in to connect" : "Add an API key");
    }

    /// <summary>Adds a new connection (assigning a unique id) and stores its key. Returns the saved connection.</summary>
    public async Task<ProviderConnection> AddAsync(ProviderConnection connection, string? apiKey)
    {
        connection.Id = UniqueId(connection.Id);
        _settings.Current.Providers.Add(connection);
        if (!string.IsNullOrWhiteSpace(apiKey))
            _secrets.Set(SecretKeys.ApiKeyFor(connection.Id), apiKey.Trim());

        await PersistAsync();
        return connection;
    }

    /// <summary>Replaces an existing connection (matched by id) with the edited copy; optionally updates the key.</summary>
    public async Task UpdateAsync(ProviderConnection connection, string? apiKey)
    {
        var index = _settings.Current.Providers.FindIndex(p => p.Id == connection.Id);
        if (index >= 0)
            _settings.Current.Providers[index] = connection;
        else
            _settings.Current.Providers.Add(connection);

        if (!string.IsNullOrWhiteSpace(apiKey))
            _secrets.Set(SecretKeys.ApiKeyFor(connection.Id), apiKey.Trim());

        await PersistAsync();
    }

    /// <summary>Stores/clears the OAuth client secret for a (Vertex) connection.</summary>
    public async Task SetClientSecretAsync(ProviderConnection connection, string? clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientSecret))
            _secrets.Delete(SecretKeys.ClientSecretFor(connection.Id));
        else
            _secrets.Set(SecretKeys.ClientSecretFor(connection.Id), clientSecret.Trim());
        await PersistAsync();
    }

    /// <summary>Runs the Google sign-in consent flow for a Vertex connection.</summary>
    public Task SignInAsync(ProviderConnection connection) => _oauth.SignInAsync(connection);

    /// <summary>Signs out of Google for a Vertex connection.</summary>
    public Task SignOutAsync(ProviderConnection connection) => _oauth.SignOutAsync(connection);

    /// <summary>Removes a connection and its stored secrets.</summary>
    public async Task RemoveAsync(ProviderConnection connection)
    {
        _settings.Current.Providers.RemoveAll(p => p.Id == connection.Id);
        _secrets.Delete(SecretKeys.ApiKeyFor(connection.Id));
        if (connection.Kind == ProviderKind.GoogleVertex)
        {
            try { await _oauth.SignOutAsync(connection); } catch { /* best effort */ }
            _secrets.Delete(SecretKeys.ClientSecretFor(connection.Id));
        }
        await PersistAsync();
    }

    public async Task SetEnabledAsync(ProviderConnection connection, bool enabled)
    {
        connection.Enabled = enabled;
        await _settings.SaveAsync();
        _registry.Invalidate();
    }

    /// <summary>Stores/clears the API key for a connection.</summary>
    public async Task SetApiKeyAsync(ProviderConnection connection, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            _secrets.Delete(SecretKeys.ApiKeyFor(connection.Id));
        else
            _secrets.Set(SecretKeys.ApiKeyFor(connection.Id), apiKey.Trim());
        await PersistAsync();
    }

    /// <summary>Saves settings, refreshes the registry, and rebuilds the card list.</summary>
    public async Task PersistAsync()
    {
        await _settings.SaveAsync();
        _registry.Invalidate();
        Reload();
    }

    private string UniqueId(string preferred)
    {
        var baseId = string.IsNullOrWhiteSpace(preferred) ? "custom" : preferred;
        if (_settings.Current.Providers.All(p => p.Id != baseId))
            return baseId;
        return $"{baseId}-{Guid.NewGuid():N}"[..Math.Min(baseId.Length + 9, 40)];
    }

    private static bool IsLocalEndpoint(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase));
}
