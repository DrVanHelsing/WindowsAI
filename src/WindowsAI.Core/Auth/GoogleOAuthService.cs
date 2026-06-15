using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Json;
using Google.Apis.Util.Store;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Auth;

/// <summary>
/// Signs the user in to Google with the OAuth 2.0 "installed app" loopback flow and hands out
/// fresh access tokens for Google Cloud / Vertex AI. The browser-based consent runs once; the
/// resulting refresh token is persisted (DPAPI-encrypted, via <see cref="ISecretStore"/>) and
/// silently exchanged for short-lived access tokens thereafter.
/// </summary>
public sealed class GoogleOAuthService
{
    // cloud-platform is the scope Vertex AI's generateContent / OpenAI-compatible endpoint requires.
    private const string CloudPlatformScope = "https://www.googleapis.com/auth/cloud-platform";

    private const string UserId = "user";

    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;

    // Cached for the process lifetime once signed in; GetAccessTokenForRequestAsync refreshes itself.
    private UserCredential? _credential;

    public GoogleOAuthService(ISettingsStore settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    /// <summary>True once a Google sign-in has completed and a refresh token is on disk.</summary>
    public bool IsSignedIn => !string.IsNullOrEmpty(_secrets.Get(SecretKeys.GoogleConnected));

    /// <summary>
    /// Launches the system browser for Google consent and stores the resulting refresh token.
    /// Requires <see cref="GoogleCloudSettings.ClientId"/> and the client secret to be set.
    /// </summary>
    public async Task SignInAsync(CancellationToken ct = default)
    {
        var flow = CreateFlow();
        var receiver = new LocalServerCodeReceiver();
        _credential = await new AuthorizationCodeInstalledApp(flow, receiver).AuthorizeAsync(UserId, ct);

        // Sentinel so IsSignedIn is cheap and independent of Google's internal token key format.
        _secrets.Set(SecretKeys.GoogleConnected, "1");
    }

    /// <summary>Revokes the stored token and clears the saved sign-in.</summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        try
        {
            if (_credential is not null)
                await _credential.RevokeTokenAsync(ct);
        }
        catch
        {
            // Best-effort revoke; we clear local state regardless.
        }

        await CreateFlow().DeleteTokenAsync(UserId, ct);
        _secrets.Delete(SecretKeys.GoogleConnected);
        _credential = null;
    }

    /// <summary>
    /// Returns a valid OAuth access token, refreshing silently if needed. Throws if the user has
    /// not signed in or the OAuth client isn't configured.
    /// </summary>
    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        _credential ??= await LoadCredentialAsync(ct);
        if (_credential is null)
            throw new InvalidOperationException("Not signed in to Google. Sign in from Settings → Google sign-in.");

        return await _credential.GetAccessTokenForRequestAsync(cancellationToken: ct);
    }

    /// <summary>Rehydrates the credential from the stored refresh token without prompting.</summary>
    private async Task<UserCredential?> LoadCredentialAsync(CancellationToken ct)
    {
        if (!IsSignedIn)
            return null;

        var flow = CreateFlow();
        var token = await flow.LoadTokenAsync(UserId, ct);
        return token is null ? null : new UserCredential(flow, UserId, token);
    }

    private GoogleAuthorizationCodeFlow CreateFlow()
    {
        var google = _settings.Current.GoogleCloud;
        var clientSecret = _secrets.Get(SecretKeys.GoogleClientSecret);

        if (string.IsNullOrWhiteSpace(google.ClientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException(
                "Google OAuth client isn't configured. Add the client id + secret in Settings → Google sign-in.");

        return new GoogleAuthorizationCodeFlow(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = google.ClientId, ClientSecret = clientSecret },
            Scopes = [CloudPlatformScope],
            DataStore = new SecretStoreDataStore(_secrets),
        });
    }
}

/// <summary>
/// An <see cref="IDataStore"/> that keeps Google's OAuth tokens in the app's encrypted
/// <see cref="ISecretStore"/> (DPAPI) rather than as plaintext files. Uses Google's own JSON
/// serializer so the stored <c>TokenResponse</c> round-trips exactly as the library expects.
/// </summary>
file sealed class SecretStoreDataStore : IDataStore
{
    private const string Prefix = "google.oauth.";

    private readonly ISecretStore _secrets;

    public SecretStoreDataStore(ISecretStore secrets) => _secrets = secrets;

    public Task StoreAsync<T>(string key, T value)
    {
        _secrets.Set(Prefix + key, NewtonsoftJsonSerializer.Instance.Serialize(value));
        return Task.CompletedTask;
    }

    public Task DeleteAsync<T>(string key)
    {
        _secrets.Delete(Prefix + key);
        return Task.CompletedTask;
    }

    public Task<T> GetAsync<T>(string key)
    {
        var json = _secrets.Get(Prefix + key);
        var value = string.IsNullOrEmpty(json)
            ? default!
            : NewtonsoftJsonSerializer.Instance.Deserialize<T>(json);
        return Task.FromResult(value);
    }

    public Task ClearAsync()
    {
        // ISecretStore can't enumerate; the only key this flow writes is the user's TokenResponse.
        _secrets.Delete(Prefix + UserTokenKey);
        return Task.CompletedTask;
    }

    // Matches Google.Apis' generated key: "{userId}-{TokenResponse FullName}".
    private const string UserTokenKey = "user-Google.Apis.Auth.OAuth2.Responses.TokenResponse";
}
