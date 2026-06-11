using WindowsAI.Core.Search;

namespace WindowsAI.Services.Search;

/// <summary>
/// Fans one overlay query out to the app / file / settings providers in parallel and
/// assembles the grouped, best-match-first result the launcher UI renders.
/// </summary>
public sealed class OverlaySearchService
{
    private const int MaxApps = 4;
    private const int MaxFiles = 8;
    private const int MaxSettings = 3;

    private readonly AppCatalog _apps;
    private readonly FileNameSearch _files;

    public OverlaySearchService(AppCatalog apps, FileNameSearch files)
    {
        _apps = apps;
        _files = files;
    }

    public async Task<OverlaySearchResults> SearchAsync(string query, CancellationToken ct = default)
    {
        query = query.Trim();
        if (query.Length == 0)
            return new OverlaySearchResults(query, null, [], [], []);

        var appsTask = _apps.SearchAsync(query, MaxApps, ct);
        var filesTask = _files.SearchAsync(query, MaxFiles, ct);
        var settings = SettingsCatalog.Search(query, MaxSettings);

        await Task.WhenAll(appsTask, filesTask).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        var apps = appsTask.Result.ToList();
        var files = filesTask.Result.ToList();
        var settingsList = settings.ToList();

        var best = PickBestMatch(query, apps, files, settingsList);
        if (best is not null)
        {
            // Best match is shown in its own section — don't repeat it in a group.
            apps.Remove(best);
            files.Remove(best);
            settingsList.Remove(best);
        }

        return new OverlaySearchResults(query, best, apps, files, settingsList);
    }

    /// <summary>
    /// Launcher-style ranking: an app the user typed the name of beats everything, then
    /// settings/files whose names start with the query, then weaker contains matches.
    /// </summary>
    public static SearchHit? PickBestMatch(
        string query, IReadOnlyList<SearchHit> apps, IReadOnlyList<SearchHit> files, IReadOnlyList<SearchHit> settings)
    {
        return apps.FirstOrDefault(a => a.Name.Equals(query, StringComparison.OrdinalIgnoreCase))
            ?? apps.FirstOrDefault(a => a.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            ?? settings.FirstOrDefault(s => s.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            ?? files.FirstOrDefault(f => f.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            ?? apps.FirstOrDefault()
            ?? files.FirstOrDefault()
            ?? settings.FirstOrDefault();
    }
}
