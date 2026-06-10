using System.Security.Cryptography;
using System.Text;
using WindowsAI.Core.Security;

namespace WindowsAI.Services.Security;

/// <summary>
/// <see cref="ISecretStore"/> backed by Windows DPAPI (current-user scope). Each secret
/// is encrypted and written to its own file under <c>%LOCALAPPDATA%\WindowsAI\secrets</c>,
/// so values are bound to the Windows user account and never stored in plaintext.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _dir;

    public DpapiSecretStore()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAI",
            "secrets");
    }

    public void Set(string key, string secret)
    {
        Directory.CreateDirectory(_dir);
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret), optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(key), protectedBytes);
    }

    public string? Get(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;

        try
        {
            var clearBytes = ProtectedData.Unprotect(
                File.ReadAllBytes(path), optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(clearBytes);
        }
        catch (CryptographicException)
        {
            // Wrong user / corrupted blob — treat as absent rather than crashing.
            return null;
        }
    }

    public void Delete(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
    }

    private string PathFor(string key)
    {
        // Hash the key to a safe, fixed-length filename.
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_dir, hash + ".bin");
    }
}
