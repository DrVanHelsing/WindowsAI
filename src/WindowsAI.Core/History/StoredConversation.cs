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

    /// <summary>
    /// Optional per-conversation overrides for the model and/or system prompt. Null (the default)
    /// means the conversation uses the app-wide active model and default system prompt. New field —
    /// older saved conversations deserialize with this null, so it's fully back-compatible.
    /// </summary>
    public ConversationOverrides? Overrides { get; set; }
}

/// <summary>
/// Per-conversation settings that take precedence over the app defaults while this conversation is
/// active. Any field left null falls back to the app-wide value.
/// </summary>
public sealed class ConversationOverrides
{
    /// <summary>Provider id for this conversation's model (paired with <see cref="ModelId"/>).</summary>
    public string? ProviderId { get; set; }

    /// <summary>Model id to use for this conversation.</summary>
    public string? ModelId { get; set; }

    /// <summary>A custom system prompt that replaces the app default for this conversation.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>True when at least one override is set (worth persisting).</summary>
    public bool HasAny =>
        !string.IsNullOrWhiteSpace(ProviderId)
        || !string.IsNullOrWhiteSpace(ModelId)
        || !string.IsNullOrWhiteSpace(SystemPrompt);
}

/// <summary>A single persisted message. <see cref="IsUser"/> distinguishes the user from the assistant.</summary>
public sealed class StoredMessage
{
    public string Author { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public bool IsUser { get; set; }
}
