using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Json;
using Google.Apis.Util.Store;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Auth;

/// <summary>
/// Signs the user in to Google with the OAuth 2.0 "installed app" loopback flow and hands out
/// fresh access tokens for Google Cloud / Vertex AI. The browser-based consent runs once per
/// Vertex <see cref="ProviderConnection"/>; the resulting refresh token is persisted (DPAPI-
/// encrypted, via <see cref="ISecretStore"/>) and silently exchanged for access tokens thereafter.
/// Everything is keyed by the connection id, so more than one Google-signed provider can coexist.
/// </summary>
public sealed class GoogleOAuthService
{
    // cloud-platform is the scope Vertex AI's generateContent / OpenAI-compatible endpoint requires.
    private const string CloudPlatformScope = "https://www.googleapis.com/auth/cloud-platform";

    private const string UserId = "user";

    private readonly ISecretStore _secrets;

    // Cached per connection for the process lifetime once signed in; the credential refreshes itself.
    private readonly ConcurrentDictionary<string, UserCredential> _credentials = new();

    public GoogleOAuthService(ISecretStore secrets) => _secrets = secrets;

    /// <summary>True once a Google sign-in has completed for this connection and a refresh token is on disk.</summary>
    public bool IsSignedIn(ProviderConnection connection) =>
        !string.IsNullOrEmpty(_secrets.Get(SecretKeys.ConnectedFor(connection.Id)));

    /// <summary>
    /// Launches the system browser for Google consent and stores the resulting refresh token.
    /// Requires the connection's <see cref="ProviderConnection.OAuthClientId"/> and client secret.
    /// </summary>
    public async Task SignInAsync(ProviderConnection connection, CancellationToken ct = default)
    {
        var flow = CreateFlow(connection);
        var receiver = new LocalServerCodeReceiver();
        var credential = await new AuthorizationCodeInstalledApp(flow, receiver).AuthorizeAsync(UserId, ct);
        _credentials[connection.Id] = credential;

        // Sentinel so IsSignedIn is cheap and independent of Google's internal token key format.
        _secrets.Set(SecretKeys.ConnectedFor(connection.Id), "1");
    }

    /// <summary>Revokes the stored token and clears the saved sign-in for this connection.</summary>
    public async Task SignOutAsync(ProviderConnection connection, CancellationToken ct = default)
    {
        try
        {
            if (_credentials.TryGetValue(connection.Id, out var credential))
                await credential.RevokeTokenAsync(ct);
        }
        catch
        {
            // Best-effort revoke; we clear local state regardless.
        }

        await CreateFlow(connection).DeleteTokenAsync(UserId, ct);
        _secrets.Delete(SecretKeys.ConnectedFor(connection.Id));
        _credentials.TryRemove(connection.Id, out _);
    }

    /// <summary>
    /// Returns a valid OAuth access token for the connection, refreshing silently if needed.
    /// Throws if the user has not signed in or the OAuth client isn't configured.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(ProviderConnection connection, CancellationToken ct = default)
    {
        if (!_credentials.TryGetValue(connection.Id, out var credential))
        {
            credential = await LoadCredentialAsync(connection, ct)
                ?? throw new InvalidOperationException(
                    "Not signed in to Google. Sign in from Settings → the provider's card.");
            _credentials[connection.Id] = credential;
        }

        return await credential.GetAccessTokenForRequestAsync(cancellationToken: ct);
    }

    /// <summary>Rehydrates the credential from the stored refresh token without prompting.</summary>
    private async Task<UserCredential?> LoadCredentialAsync(ProviderConnection connection, CancellationToken ct)
    {
        if (!IsSignedIn(connection))
            return null;

        var flow = CreateFlow(connection);
        var token = await flow.LoadTokenAsync(UserId, ct);
        return token is null ? null : new UserCredential(flow, UserId, token);
    }

    private GoogleAuthorizationCodeFlow CreateFlow(ProviderConnection connection)
    {
        var clientId = connection.OAuthClientId;
        var clientSecret = _secrets.Get(SecretKeys.ClientSecretFor(connection.Id));

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException(
                "Google OAuth client isn't configured. Add the client id + secret on the provider's card.");

        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = [CloudPlatformScope],
            DataStore = new SecretStoreDataStore(_secrets, SecretKeys.OAuthPrefixFor(connection.Id)),
        });
    }
}

/// <summary>
/// An <see cref="IDataStore"/> that keeps Google's OAuth tokens in the app's encrypted
/// <see cref="ISecretStore"/> (DPAPI) rather than as plaintext files, under a per-connection
/// prefix. Uses Google's own JSON serializer so the stored <c>TokenResponse</c> round-trips
/// exactly as the library expects.
/// </summary>
file sealed class SecretStoreDataStore : IDataStore
{
    private readonly ISecretStore _secrets;
    private readonly string _prefix;

    public SecretStoreDataStore(ISecretStore secrets, string prefix)
    {
        _secrets = secrets;
        _prefix = prefix;
    }

    public Task StoreAsync<T>(string key, T value)
    {
        _secrets.Set(_prefix + key, NewtonsoftJsonSerializer.Instance.Serialize(value));
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        _secrets.Delete(_prefix + key);
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var json = _secrets.Get(_prefix + key);
        var value = string.IsNullOrEmpty(json)
            ? default!
            : NewtonsoftJsonSerializer.Instance.Deserialize<T>(json);
        return Task.FromResult(value);
    }

    public Task ClearAsync()
    {
        // ISecretStore can't enumerate; the only key this flow writes is the user's TokenResponse.
        _secrets.Delete(_prefix + UserTokenKey);
        return Task.CompletedTask;
    }

    // Matches Google.Apis' generated key: "{userId}-{TokenResponse FullName}".
    private const string UserTokenKey = "user-Google.Apis.Auth.OAuth2.Responses.TokenResponse";
}
