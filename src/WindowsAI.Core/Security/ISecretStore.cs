namespace WindowsAI.Core.Security;

/// <summary>
/// Secure, per-user storage for secrets such as cloud API keys. The Windows
/// implementation encrypts values with DPAPI so they never sit in plaintext on disk.
/// </summary>
public interface ISecretStore
{
    /// <summary>Stores (or overwrites) a secret under <paramref name="key"/>.</summary>
    void Set(string key, string secret);

    /// <summary>Returns the secret for <paramref name="key"/>, or null if absent.</summary>
    string? Get(string key);

    /// <summary>Removes the secret if present.</summary>
    void Delete(string key);
}
