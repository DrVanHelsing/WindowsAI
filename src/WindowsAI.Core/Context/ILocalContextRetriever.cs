namespace WindowsAI.Core.Context;

/// <summary>
/// Finds local documents relevant to a chat prompt so they can be injected as model
/// context (local-first RAG). Implementations are platform-specific — on Windows the
/// system search index — and must be fast and failure-silent: retrieval is an
/// enhancement, never a reason a message fails to send.
/// </summary>
public interface ILocalContextRetriever
{
    /// <summary>
    /// Returns up to <paramref name="maxResults"/> documents relevant to
    /// <paramref name="query"/>, best match first. Returns an empty list on any failure.
    /// </summary>
    Task<IReadOnlyList<LocalDocument>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken = default);
}
