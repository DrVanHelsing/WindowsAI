using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.UI.Dispatching;
using WindowsAI.Core.Context;

namespace WindowsAI_App.Agents;

/// <summary>
/// Builds the agent's tools for a single send. Tools are created per-exchange (not shared)
/// so each <c>ChatViewModel</c> instance — the shell's and the overlay's independent one —
/// drives its own UI state and accumulates its own set of surfaced files.
/// </summary>
public static class AgentToolFactory
{
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
    public static IReadOnlyList<AITool> CreateLocalTools(
        ILocalContextRetriever retriever,
        DispatcherQueue dispatcher,
        Action<bool> setSearching,
        Action<IReadOnlyList<LocalDocument>> addSources)
    {
        // Files surfaced by search_my_pc this exchange — the only paths open_path may open.
        // Tool calls within one response are sequential, so no locking is needed.
        var surfaced = new List<LocalDocument>();

        void OnUi(Action action)
        {
            if (dispatcher.HasThreadAccess) action();
            else dispatcher.TryEnqueue(() => action());
        }

        async Task<string> SearchMyPc(string query, int maxResults = 4)
        {
            OnUi(() => setSearching(true));
            IReadOnlyList<LocalDocument> docs;
            try
            {
                docs = await retriever.SearchAsync(query, Math.Clamp(maxResults, 1, 8));
            }
            finally
            {
                OnUi(() => setSearching(false));
            }

            if (docs.Count > 0)
            {
                surfaced.AddRange(docs);
                OnUi(() => addSources(docs));
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
}
