namespace WindowsAI.Core.Context;

/// <summary>
/// Decorates a keyword retriever with semantic re-ranking. It pulls a larger candidate
/// pool from the inner retriever, embeds the query and every candidate, and returns the
/// top matches by cosine similarity. When embeddings are unavailable it returns the inner
/// retriever's own ordering unchanged — re-ranking is a best-effort enhancement that must
/// never become a reason retrieval fails.
/// </summary>
public sealed class RerankingContextRetriever : ILocalContextRetriever
{
    // Over-fetch from the keyword retriever so there is something to re-order.
    private const int CandidateMultiplier = 4;
    private const int MinCandidates = 8;

    private readonly ILocalContextRetriever _inner;
    private readonly IEmbeddingClient _embeddings;

    public RerankingContextRetriever(ILocalContextRetriever inner, IEmbeddingClient embeddings)
    {
        _inner = inner;
        _embeddings = embeddings;
    }

    public async Task<IReadOnlyList<LocalDocument>> SearchAsync(
        string query, int maxResults, CancellationToken cancellationToken = default)
    {
        var poolSize = Math.Max(MinCandidates, maxResults * CandidateMultiplier);
        var candidates = await _inner.SearchAsync(query, poolSize, cancellationToken);

        // Nothing to gain if the keyword pass already returned no more than we need.
        if (candidates.Count <= maxResults)
            return candidates;

        // One request embeds the query (index 0) plus every candidate.
        var inputs = new List<string>(candidates.Count + 1) { query };
        foreach (var doc in candidates)
            inputs.Add(EmbedText(doc));

        var vectors = await _embeddings.EmbedAsync(inputs, cancellationToken);
        if (vectors is null || vectors.Count != inputs.Count)
            return candidates.Take(maxResults).ToList(); // fall back to keyword order

        var queryVector = vectors[0];
        return candidates
            .Select((doc, i) => (doc, score: Cosine(queryVector, vectors[i + 1])))
            .OrderByDescending(x => x.score)
            .Take(maxResults)
            .Select(x => x.doc)
            .ToList();
    }

    /// <summary>Text embedded for a candidate: file name plus its content snippet.</summary>
    private static string EmbedText(LocalDocument doc) =>
        string.IsNullOrEmpty(doc.Snippet) ? doc.Name : $"{doc.Name}\n{doc.Snippet}";

    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length)
            return 0;

        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        return (normA == 0 || normB == 0) ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }
}
