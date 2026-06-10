namespace WindowsAI.Core.History;

/// <summary>A persisted chat session: an ordered list of messages plus display metadata.</summary>
public sealed class StoredConversation
{
    /// <summary>Stable, filename-safe identifier.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    /// <summary>Human-readable title, typically derived from the first user message.</summary>
    public string Title { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public List<StoredMessage> Messages { get; set; } = new();
}

/// <summary>A single persisted message. <see cref="IsUser"/> distinguishes the user from the assistant.</summary>
public sealed class StoredMessage
{
    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public bool IsUser { get; set; }
}
