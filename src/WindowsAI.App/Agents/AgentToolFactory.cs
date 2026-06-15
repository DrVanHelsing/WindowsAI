using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.UI.Dispatching;
using WindowsAI.Core.Context;
using WindowsAI.Core.Tools;

namespace WindowsAI_App.Agents;

/// <summary>
/// Builds the agent's tools for a single send. Tools are created per-exchange (not shared)
/// so each <c>ChatViewModel</c> instance — the shell's and the overlay's independent one —
/// drives its own UI state and accumulates its own set of surfaced files.
/// </summary>
public static class AgentToolFactory
{
    /// <summary>Marshals an action onto the owning view model's dispatcher thread.</summary>
    private static void OnUi(DispatcherQueue dispatcher, Action action)
    {
        if (dispatcher.HasThreadAccess) action();
        else dispatcher.TryEnqueue(() => action());
    }

    /// <summary>
    /// Keyless utility tools available on every send. Currently <c>get_current_datetime</c>, which
    /// gives the model an accurate clock (it otherwise has no notion of "now"). Surfaces a step via
    /// <paramref name="onActivity"/>.
    /// </summary>
    public static IReadOnlyList<AITool> CreateUtilityTools(
        DispatcherQueue dispatcher,
        Action<ToolActivity> onActivity)
    {
        string GetCurrentDateTime()
        {
            OnUi(dispatcher, () => onActivity(new ToolActivity("", "Checked the current time")));
            var now = DateTimeOffset.Now;
            return $"Local time: {now:dddd, d MMMM yyyy, HH:mm} ({now:zzz}). UTC: {now.UtcDateTime:yyyy-MM-dd HH:mm} UTC.";
        }

        return new AITool[]
        {
            AIFunctionFactory.Create(
                GetCurrentDateTime,
                name: "get_current_datetime",
                description: "Get the current local date and time (and UTC). Use this whenever the user asks " +
                             "about the date, the day of week, or the current time, or when answering needs today's date."),
        };
    }

    /// <summary>
    /// The <c>search_web</c> tool, backed by Tavily Search. Surfaces each query (and its
    /// result links) via <paramref name="onActivity"/> so the user sees what was searched.
    /// </summary>
    public static IReadOnlyList<AITool> CreateWebTools(
        WebSearchService webSearch,
        DispatcherQueue dispatcher,
        Action<ToolActivity> onActivity)
    {
        async Task<string> SearchWeb(string query, int maxResults = 5)
        {
            IReadOnlyList<WebResult> results;
            try
            {
                results = await webSearch.SearchAsync(query, Math.Clamp(maxResults, 1, 10));
            }
            catch (Exception ex)
            {
                OnUi(dispatcher, () => onActivity(new ToolActivity("", $"Web search failed · “{query}”")));
                return $"Web search failed: {ex.Message}";
            }

            OnUi(dispatcher, () => onActivity(new ToolActivity(
                "",
                $"Searched the web · “{query}”",
                results.Count > 0 ? results : null)));

            return FormatWebResults(query, results);
        }

        return new AITool[]
        {
            AIFunctionFactory.Create(
                SearchWeb,
                name: "search_web",
                description: "Search the public web for current information and return the top results with " +
                             "titles, URLs, and snippets. Use this for recent events, facts that may have changed, " +
                             "or anything not in your training data. 'query' is a natural-language search; " +
                             "'maxResults' defaults to 5. Cite sources by title or URL."),
        };
    }

    /// <summary>
    /// Creates the local-machine tools: <c>search_my_pc</c> (Windows Search RAG) and
    /// <c>open_path</c> (opens a previously-found file/folder). UI callbacks are marshalled
    /// onto <paramref name="dispatcher"/> because the function-invocation pipeline may resume
    /// tool delegates off the UI thread.
    /// </summary>
    /// <param name="retriever">Backs <c>search_my_pc</c>.</param>
    /// <param name="dispatcher">UI dispatcher for the owning view model's thread.</param>
    /// <param name="setSearching">Toggles the "Searching your PC…" indicator.</param>
    /// <param name="addSources">Appends found files to the live reply's source chips.</param>
    /// <param name="onActivity">Surfaces a step line ("Searched your PC · …") for each search.</param>
    public static IReadOnlyList<AITool> CreateLocalTools(
        ILocalContextRetriever retriever,
        DispatcherQueue dispatcher,
        Action<bool> setSearching,
        Action<IReadOnlyList<LocalDocument>> addSources,
        Action<ToolActivity> onActivity)
    {
        // Files surfaced by search_my_pc this exchange — the only paths open_path may open.
        // Tool calls within one response are sequential, so no locking is needed.
        var surfaced = new List<LocalDocument>();

        async Task<string> SearchMyPc(string query, int maxResults = 4)
        {
            OnUi(dispatcher, () => setSearching(true));
            IReadOnlyList<LocalDocument> docs;
            try
            {
                docs = await retriever.SearchAsync(query, Math.Clamp(maxResults, 1, 8));
            }
            finally
            {
                OnUi(dispatcher, () => setSearching(false));
            }

            OnUi(dispatcher, () => onActivity(new ToolActivity("", $"Searched your PC · “{query}”")));

            if (docs.Count > 0)
            {
                surfaced.AddRange(docs);
                OnUi(dispatcher, () => addSources(docs));
            }

            return FormatResults(query, docs);
        }

        string OpenPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "No path was provided.";

            var match = surfaced.FirstOrDefault(d => MatchesPath(d, path));
            if (match is null)
                return $"Refused: \"{path}\" was not among the files found by search_my_pc. " +
                       "Only files surfaced by a search this conversation can be opened.";

            try
            {
                OnUi(dispatcher, () => onActivity(new ToolActivity("", $"Opened {match.Name}")));
                Process.Start(new ProcessStartInfo(match.Path) { UseShellExecute = true });
                return $"Opened {match.Name}.";
            }
            catch (Exception ex)
            {
                return $"Failed to open {match.Name}: {ex.Message}";
            }
        }

        return new AITool[]
        {
            AIFunctionFactory.Create(
                SearchMyPc,
                name: "search_my_pc",
                description: "Search the user's PC (the Windows Search index) for files relevant to a " +
                             "query and return the best matches with short content excerpts. Use this " +
                             "when the user asks about their own files, documents, or local content. " +
                             "'query' is a natural-language search; 'maxResults' defaults to 4."),

            AIFunctionFactory.Create(
                OpenPath,
                name: "open_path",
                description: "Open a file or folder in its default Windows application. For safety you may " +
                             "only pass a path (or file name) that a previous search_my_pc call returned. " +
                             "Use after finding a file the user asked you to open."),
        };
    }

    /// <summary>Matches by full path (case-insensitive) or, as a fallback, by file name.</summary>
    private static bool MatchesPath(LocalDocument doc, string requested)
    {
        var req = requested.Trim().Trim('"');
        if (string.Equals(doc.Path, req, StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            if (string.Equals(Path.GetFullPath(doc.Path), Path.GetFullPath(req), StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // requested wasn't a valid path — fall through to name match.
        }

        return string.Equals(doc.Name, req, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Formats search hits for the model: numbered paths with excerpts.</summary>
    private static string FormatResults(string query, IReadOnlyList<LocalDocument> documents)
    {
        if (documents.Count == 0)
            return $"No files on this PC matched \"{query}\".";

        var sb = new StringBuilder();
        sb.AppendLine($"Found {documents.Count} file(s) for \"{query}\". Cite file names when you use them.");
        for (int i = 0; i < documents.Count; i++)
        {
            sb.AppendLine();
            sb.AppendLine($"[{i + 1}] {documents[i].Path}");
            sb.AppendLine(documents[i].Snippet);
        }

        return sb.ToString();
    }

    /// <summary>Formats web hits for the model: numbered title + url + snippet.</summary>
    private static string FormatWebResults(string query, IReadOnlyList<WebResult> results)
    {
        if (results.Count == 0)
            return $"No web results found for \"{query}\".";

        var sb = new StringBuilder();
        sb.AppendLine($"Top {results.Count} web result(s) for \"{query}\". Cite sources by title or URL.");
        for (int i = 0; i < results.Count; i++)
        {
            sb.AppendLine();
            sb.AppendLine($"[{i + 1}] {results[i].Title}");
            sb.AppendLine(results[i].Url);
            if (!string.IsNullOrWhiteSpace(results[i].Snippet))
                sb.AppendLine(results[i].Snippet);
        }

        return sb.ToString();
    }
}
