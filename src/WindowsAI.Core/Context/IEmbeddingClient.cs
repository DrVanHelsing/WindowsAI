namespace WindowsAI.Core.Context;

/// <summary>
/// Produces embedding vectors for short texts, used to re-rank local search results by
/// semantic similarity to the query. Implementations must be failure-silent: any error
/// (model not pulled, server down, malformed response) returns <c>null</c> so the caller
/// can fall back to the original keyword ordering.
/// </summary>
public interface IEmbeddingClient
{
    /// <summary>
    /// Embeds each input string. Returns one vector per input, aligned by index, or
    /// <c>null</c> if embeddings are unavailable.
    /// </summary>
    Task<IReadOnlyList<float[]>?> EmbedAsync(
        IReadOnlyList<string> inputs, CancellationToken cancellationToken = default);
}
