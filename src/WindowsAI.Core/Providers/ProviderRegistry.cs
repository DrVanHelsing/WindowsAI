using WindowsAI.Core.Auth;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Default <see cref="IProviderRegistry"/>: instantiates one provider per enabled
/// <see cref="ProviderConnection"/> by kind, caching them until <see cref="Invalidate"/> is called.
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;
    private readonly GoogleOAuthService _oauth;
    private readonly object _lock = new();

    private List<IChatProvider> _providers = new();

    public ProviderRegistry(ISettingsStore settings, ISecretStore secrets, GoogleOAuthService oauth)
    {
        _settings = settings;
        _secrets = secrets;
        _oauth = oauth;
        Build();
    }

    public event EventHandler? Changed;

    public IReadOnlyList<IChatProvider> All
    {
        get { lock (_lock) return _providers.ToList(); }
    }

    public IChatProvider? GetById(string id) => All.FirstOrDefault(p => p.Id == id);

    public IChatProvider? GetConfigured(string id)
    {
        var provider = GetById(id);
        return provider is { IsConfigured: true } ? provider : null;
    }

    public void Invalidate()
    {
        Build();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Build()
    {
        var built = new List<IChatProvider>();
        foreach (var connection in _settings.Current.Providers)
        {
            if (!connection.Enabled || string.IsNullOrWhiteSpace(connection.Id))
                continue;

            IChatProvider provider = connection.Kind switch
            {
                ProviderKind.Ollama => new OllamaConnectionProvider(connection),
                ProviderKind.GoogleVertex => new VertexConnectionProvider(connection, _oauth),
                _ => new OpenAiConnectionProvider(connection, _secrets),
            };
            built.Add(provider);
        }

        lock (_lock)
            _providers = built;
    }
}
