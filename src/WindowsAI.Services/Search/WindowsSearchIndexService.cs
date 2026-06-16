using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WindowsAI.Services.Search;

/// <summary>Snapshot of the Windows Search indexer's state for the "Search my PC" / PC Indexing UI.</summary>
public sealed record IndexStatus(
    bool Available,
    long ItemsIndexed,
    long ItemsRemaining,
    string? CurrentItem,
    string? Message)
{
    public bool IsUpToDate => Available && ItemsRemaining == 0;
    public static IndexStatus Unavailable(string message) => new(false, 0, 0, null, message);
}

/// <summary>
/// Manages the folders in the <b>Windows Search</b> index scope and reads the indexer's live
/// status, so the app can offer "Search my PC" over exactly the locations the user chooses. The app
/// builds no index of its own — it drives the OS Search Manager COM API
/// (CSearchManager → SystemIndex catalog → crawl-scope manager) and queries item counts for a
/// progress indicator.
///
/// <para>Every call is failure-silent: if Windows Search is disabled or the COM API is unavailable,
/// status reports <see cref="IndexStatus.Available"/> = false and mutations return false rather than
/// throwing — matching the retriever's "retrieval is an enhancement, never a crash" contract.</para>
/// </summary>
public sealed class WindowsSearchIndexService
{
    private const string SystemIndexCatalog = "SystemIndex";

    // FOLLOW_FLAGS.FF_INDEXCOMPLEXURLS — index complex (deep) URLs under the rule.
    private const uint FF_INDEXCOMPLEXURLS = 0x1;

    private static readonly Guid CLSID_CSearchManager = new("7D096C5F-AC08-4F1F-BEB7-5C22C517CE39");

    /// <summary>Reads the indexer's item counts and current activity. Never throws.</summary>
    public IndexStatus GetStatus()
    {
        ISearchManager? manager = null;
        ISearchCatalogManager? catalog = null;
        try
        {
            manager = CreateManager();
            catalog = manager.GetCatalog(SystemIndexCatalog);

            long indexed = catalog.NumberOfItems();
            catalog.NumberOfItemsToIndex(out var incremental, out var notification, out var highPriority);
            long remaining = (long)incremental + notification + highPriority;

            string? current = null;
            try { current = catalog.URLBeingIndexed(); } catch { /* optional */ }

            return new IndexStatus(true, indexed, remaining,
                string.IsNullOrWhiteSpace(current) ? null : current, null);
        }
        catch (Exception ex)
        {
            return IndexStatus.Unavailable(Describe(ex));
        }
        finally
        {
            Release(catalog);
            Release(manager);
        }
    }

    /// <summary>Adds a folder to the index scope (user scope rule). Returns true on success.</summary>
    public bool AddFolder(string path) =>
        Mutate(csm => csm.AddUserScopeRule(ToUrl(path), fInclude: 1, fOverrideChildren: 0, FF_INDEXCOMPLEXURLS));

    /// <summary>Removes a previously-added folder rule from the index scope. Returns true on success.</summary>
    public bool RemoveFolder(string path) =>
        Mutate(csm => csm.RemoveScopeRule(ToUrl(path)));

    /// <summary>True if the folder is currently within the crawl scope (best-effort; false on error).</summary>
    public bool IsIncluded(string path)
    {
        ISearchManager? manager = null;
        ISearchCatalogManager? catalog = null;
        ISearchCrawlScopeManager? scope = null;
        try
        {
            manager = CreateManager();
            catalog = manager.GetCatalog(SystemIndexCatalog);
            scope = catalog.GetCrawlScopeManager();
            return scope.IncludedInCrawlScope(ToUrl(path)) != 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            Release(scope);
            Release(catalog);
            Release(manager);
        }
    }

    /// <summary>Opens the Windows "Indexing Options" control panel so the user can manage the index directly.</summary>
    public void OpenIndexingOptions()
    {
        try
        {
            Process.Start(new ProcessStartInfo("control.exe", "srchadmin.dll") { UseShellExecute = true });
        }
        catch
        {
            // Best effort — nothing to do if the control panel can't launch.
        }
    }

    private bool Mutate(Action<ISearchCrawlScopeManager> action)
    {
        ISearchManager? manager = null;
        ISearchCatalogManager? catalog = null;
        ISearchCrawlScopeManager? scope = null;
        try
        {
            manager = CreateManager();
            catalog = manager.GetCatalog(SystemIndexCatalog);
            scope = catalog.GetCrawlScopeManager();
            action(scope);
            scope.SaveAll();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            Release(scope);
            Release(catalog);
            Release(manager);
        }
    }

    private static ISearchManager CreateManager()
    {
        var type = Type.GetTypeFromCLSID(CLSID_CSearchManager)
            ?? throw new InvalidOperationException("Windows Search is not available on this PC.");
        return (ISearchManager)(Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Could not create the Windows Search manager."));
    }

    /// <summary>A folder path as the file:// URL the crawl-scope manager expects.</summary>
    private static string ToUrl(string path)
    {
        var full = Path.GetFullPath(path).Replace('\\', '/');
        return "file:///" + full;
    }

    private static string Describe(Exception ex) => ex switch
    {
        COMException => "Windows Search is turned off or unavailable.",
        InvalidOperationException => ex.Message,
        _ => "Windows Search could not be reached.",
    };

    private static void Release(object? comObject)
    {
        try
        {
            if (comObject is not null && Marshal.IsComObject(comObject))
                Marshal.FinalReleaseComObject(comObject);
        }
        catch
        {
            // Ignore release failures.
        }
    }
}

// ════════════════════════════════════════════════════════════════════════════════════════════════
//  Windows Search COM interop. Method order MUST match the searchapi.h vtable exactly; methods the
//  app never calls are declared as reserved single-slot placeholders. Sourced from Microsoft's own
//  C# transcription (PowerToys indexer plugin + Microsoft.Search.Interop).
// ════════════════════════════════════════════════════════════════════════════════════════════════

[ComImport, Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF69"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISearchManager
{
    void _01_GetIndexerVersionStr();
    void _02_GetIndexerVersion();
    void _03_GetParameter();
    void _04_SetParameter();
    void _05_get_ProxyName();
    void _06_get_BypassList();
    void _07_SetProxy();

    [return: MarshalAs(UnmanagedType.Interface)]
    ISearchCatalogManager GetCatalog([MarshalAs(UnmanagedType.LPWStr)] string pszCatalog);
}

[ComImport, Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF50"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISearchCatalogManager
{
    void _01_get_Name();
    void _02_GetParameter();
    void _03_SetParameter();
    void _04_GetCatalogStatus();
    void _05_Reset();
    void _06_Reindex();
    void _07_ReindexMatchingURLs();
    void _08_ReindexSearchRoot();
    void _09_put_ConnectTimeout();
    void _10_get_ConnectTimeout();
    void _11_put_DataTimeout();
    void _12_get_DataTimeout();

    int NumberOfItems();

    void NumberOfItemsToIndex(out int plIncrementalCount, out int plNotificationQueue, out int plHighPriorityQueue);

    [return: MarshalAs(UnmanagedType.LPWStr)]
    string URLBeingIndexed();

    void _16_GetURLIndexingState();
    void _17_GetPersistentItemsChangedSink();
    void _18_RegisterViewForNotification();
    void _19_GetItemsChangedSink();
    void _20_UnregisterViewForNotification();
    void _21_SetExtensionClusion();
    void _22_EnumerateExcludedExtensions();
    void _23_GetQueryHelper();
    void _24_put_DiacriticSensitivity();
    void _25_get_DiacriticSensitivity();

    [return: MarshalAs(UnmanagedType.Interface)]
    ISearchCrawlScopeManager GetCrawlScopeManager();
}

[ComImport, Guid("AB310581-AC80-11D1-8DF3-00C04FB6EF55"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISearchCrawlScopeManager
{
    void _01_AddDefaultScopeRule();
    void _02_AddRoot();
    void _03_RemoveRoot();
    void _04_EnumerateRoots();
    void _05_AddHierarchicalScope();

    void AddUserScopeRule(
        [MarshalAs(UnmanagedType.LPWStr)] string pszUrl, int fInclude, int fOverrideChildren, uint fFollowFlags);

    void RemoveScopeRule([MarshalAs(UnmanagedType.LPWStr)] string pszRule);

    void _08_EnumerateScopeRules();
    void _09_HasParentScopeRule();
    void _10_HasChildScopeRule();

    int IncludedInCrawlScope([MarshalAs(UnmanagedType.LPWStr)] string pszUrl);

    void _12_IncludedInCrawlScopeEx();
    void _13_RevertToDefaultScopes();

    void SaveAll();
}
