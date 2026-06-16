using Microsoft.Extensions.AI;
using OllamaSharp;
using WindowsAI.Core.Configuration;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Local inference via an Ollama server, configured from a <see cref="ProviderConnection"/>
/// (<see cref="ProviderConnection.BaseUrl"/> is the endpoint). <see cref="OllamaApiClient"/>
/// implements <see cref="IChatClient"/> directly; it is cached for the instance's lifetime and
/// just re-pointed at the requested model. Always "configured" — no key required.
/// </summary>
public sealed class OllamaConnectionProvider : IChatProvider
{
    private readonly ProviderConnection _connection;
    private OllamaApiClient? _client;

    public OllamaConnectionProvider(ProviderConnection connection) => _connection = connection;

    public string Id => _connection.Id;
    public string DisplayName => _connection.DisplayName;
    public bool IsLocal => true;
    public bool RequiresApiKey => false;
    public bool IsConfigured => true;
    public bool SupportsVision => _connection.SupportsVision;
    public string? DefaultModelId => _connection.DefaultModelId;

    public IChatClient GetClient(string modelId)
    {
        _client ??= new OllamaApiClient(new Uri(_connection.BaseUrl));
        _client.SelectedModel = modelId;
        return _client;
    }
}
