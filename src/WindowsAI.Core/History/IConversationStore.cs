namespace WindowsAI.Core.History;

/// <summary>Persists and retrieves <see cref="StoredConversation"/> records.</summary>
public interface IConversationStore
{
    /// <summary>All saved conversations, most-recently-updated first.</summary>
    Task<IReadOnlyList<StoredConversation>> ListAsync(CancellationToken ct = default);

    /// <summary>Loads a single conversation by id, or null if it no longer exists.</summary>
    Task<StoredConversation?> GetAsync(string id, CancellationToken ct = default);

    /// <summary>Creates or overwrites a conversation.</summary>
    Task SaveAsync(StoredConversation conversation, CancellationToken ct = default);

    /// <summary>Removes a conversation. No-op if it does not exist.</summary>
    Task DeleteAsync(string id, CancellationToken ct = default);
}
