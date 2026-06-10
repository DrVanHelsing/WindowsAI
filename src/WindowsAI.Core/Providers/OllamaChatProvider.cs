using Microsoft.Extensions.AI;
using OllamaSharp;
using WindowsAI.Core.Configuration;

namespace WindowsAI.Core.Providers;

/// <summary>
/// Local inference via Ollama. <see cref="OllamaApiClient"/> implements
/// <see cref="IChatClient"/> directly. The client is cached and re-pointed at the
/// current endpoint/model on each call. Always "configured" — no key required.
/// </summary>
public sealed class OllamaChatProvider : IChatProvider
{
    private readonly ISettingsStore _settings;
    private OllamaApiClient? _client;
    private string? _endpoint;

    public OllamaChatProvider(ISettingsStore settings) => _settings = settings;

    public string Id => "ollama";
    public string DisplayName => "Ollama (local)";
    public bool IsLocal => true;
    public bool RequiresApiKey => false;
    public bool IsConfigured => true;

    public IChatClient GetClient(string modelId)
    {
        var endpoint = _settings.Current.LocalModel.Endpoint;

        // Recreate only when the endpoint changes; just re-select the model otherwise.
        if (_client is null || _endpoint != endpoint)
        {
            _client = new OllamaApiClient(new Uri(endpoint));
            _endpoint = endpoint;
        }

        _client.SelectedModel = modelId;
        return _client;
    }
}
