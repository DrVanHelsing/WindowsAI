namespace WindowsAI.Core.Search;

public enum SearchHitKind
{
    App,
    File,
    Folder,
    Setting,
}

/// <summary>One launchable result in the overlay's live search.</summary>
/// <remarks>
/// Plain class with get-only properties (not a record): types reachable from x:Bind
/// item templates must not look settable to the WinUI XamlTypeInfo generator.
/// </remarks>
public sealed class SearchHit
{
    public SearchHit(SearchHitKind kind, string name, string detail, string target)
    {
        Kind = kind;
        Name = name;
        Detail = detail;
        Target = target;
    }

    public SearchHitKind Kind { get; }

    /// <summary>Display name (app name, file name, setting name).</summary>
    public string Name { get; }

    /// <summary>Subtitle: containing folder for files, "App" / "System settings" otherwise.</summary>
    public string Detail { get; }

    /// <summary>
    /// What to ShellExecute: a file/folder path, <c>shell:AppsFolder\{AUMID}</c> for apps,
    /// or an <c>ms-settings:</c> URI for settings pages.
    /// </summary>
    public string Target { get; }
}

/// <summary>Grouped results for one live query, best match already factored out.</summary>
public sealed class OverlaySearchResults
{
    public OverlaySearchResults(
        string query,
        SearchHit? bestMatch,
        IReadOnlyList<SearchHit> apps,
        IReadOnlyList<SearchHit> files,
        IReadOnlyList<SearchHit> settings)
    {
        Query = query;
        BestMatch = bestMatch;
        Apps = apps;
        Files = files;
        Settings = settings;
    }

    public string Query { get; }
    public SearchHit? BestMatch { get; }
    public IReadOnlyList<SearchHit> Apps { get; }
    public IReadOnlyList<SearchHit> Files { get; }
    public IReadOnlyList<SearchHit> Settings { get; }

    public bool IsEmpty => BestMatch is null && Apps.Count == 0 && Files.Count == 0 && Settings.Count == 0;
}
