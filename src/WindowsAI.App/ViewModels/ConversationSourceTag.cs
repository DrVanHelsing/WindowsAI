using WindowsAI.Core.History;

namespace WindowsAI_App.ViewModels;

/// <summary>
/// Display data for a conversation's origin chip (glyph + label). A conversation can show more
/// than one — a Visual Intelligence exchange escalated to chat shows both "Visual Intelligence"
/// and "Chat". Get-only so it's safe to reach from x:Bind item templates.
/// </summary>
public sealed class ConversationSourceTag
{
    private ConversationSourceTag(string label, string glyph)
    {
        Label = label;
        Glyph = glyph;
    }

    /// <summary>Short origin name shown on the chip.</summary>
    public string Label { get; }

    /// <summary>Segoe Fluent glyph for the origin.</summary>
    public string Glyph { get; }

    // Segoe Fluent code points: Camera (E722), Edit/pencil (E70F), Message (E8BD).
    private static ConversationSourceTag For(ConversationSource source) => source switch
    {
        ConversationSource.VisualIntelligence => new("Visual Intelligence", ""),
        ConversationSource.WritingTools       => new("Writing Tools", ""),
        _                                     => new("Chat", ""),
    };

    /// <summary>
    /// Maps the stored origins to chip data, de-duped and in stored order. Plain chat conversations
    /// (empty, or <see cref="ConversationSource.Chat"/> only) get no chips — a badge is only worth
    /// showing once a conversation has a non-chat origin (Visual Intelligence / Writing Tools). In
    /// that case every tag is shown, so an escalated VI→chat exchange renders two chips.
    /// </summary>
    public static IReadOnlyList<ConversationSourceTag> From(IEnumerable<ConversationSource>? sources)
    {
        if (sources is null)
            return [];

        var ordered = sources.Distinct().ToList();
        if (!ordered.Any(s => s != ConversationSource.Chat))
            return [];

        return ordered.Select(For).ToList();
    }
}

