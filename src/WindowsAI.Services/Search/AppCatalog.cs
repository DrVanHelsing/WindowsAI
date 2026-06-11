using WindowsAI.Core.Search;

namespace WindowsAI.Services.Search;

/// <summary>
/// Installed-app search over <c>shell:AppsFolder</c> — the same virtual folder Windows
/// Search and the Start menu use, so it covers both Win32 (.lnk) and packaged apps with
/// one enumeration. Entries are cached; launching is a ShellExecute of
/// <c>shell:AppsFolder\{AppUserModelID}</c>.
/// </summary>
public sealed class AppCatalog
{
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private List<(string Name, string Target)>? _entries;
    private DateTime _loadedAt;

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0)
            return [];

        // COM enumeration is synchronous — keep it off the caller's (UI) thread.
        return await Task.Run(() =>
        {
            var entries = GetEntries();
            ct.ThrowIfCancellationRequested();

            // Prefix matches first (what a launcher user expects), then word-boundary-ish
            // contains matches, both stable-ordered by name.
            var prefix = entries
                .Where(e => e.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);
            var contains = entries
                .Where(e => !e.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                         && e.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase);

            return (IReadOnlyList<SearchHit>)prefix.Concat(contains)
                .Take(maxResults)
                .Select(e => new SearchHit(SearchHitKind.App, e.Name, "App", $@"shell:AppsFolder\{e.Target}"))
                .ToList();
        }, ct);
    }

    /// <summary>Number of installed apps the catalog knows about (forces a load).</summary>
    public int Count => GetEntries().Count;

    private List<(string Name, string Target)> GetEntries()
    {
        lock (_gate)
        {
            if (_entries is not null && DateTime.UtcNow - _loadedAt < CacheLifetime)
                return _entries;

            _entries = Enumerate();
            _loadedAt = DateTime.UtcNow;
            return _entries;
        }
    }

    private static List<(string Name, string Target)> Enumerate()
    {
        var result = new List<(string, string)>();
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null)
                return result;

            dynamic shell = Activator.CreateInstance(shellType)!;
            var folder = shell.NameSpace("shell:AppsFolder");
            foreach (var item in folder.Items())
            {
                string name = item.Name;
                string target = item.Path;
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(target))
                    result.Add((name, target));
            }
        }
        catch
        {
            // Shell COM unavailable (rare) — app search just returns nothing.
        }

        return result;
    }
}
