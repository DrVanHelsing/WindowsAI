namespace WindowsAI.Core.Writing;

/// <summary>The transforms the writing-tools surface can apply to a text selection.</summary>
public enum WritingActionKind
{
    Proofread,
    Rewrite,
    Friendly,
    Professional,
    Concise,
    Summarize,
    KeyPoints,
}

/// <summary>
/// One writing-tools transform: how it appears in the menu (label + Segoe Fluent glyph) and
/// the natural-language instruction handed to the model. Kept tiny and declarative so the
/// menu, the prompt, and the tests all read from the same source of truth.
/// </summary>
public sealed record WritingAction(
    WritingActionKind Kind,
    string Label,
    string Glyph,
    string Instruction)
{
    /// <summary>The actions offered, in menu order. Proofread leads — it's the safest, most-used.</summary>
    public static IReadOnlyList<WritingAction> All { get; } =
    [
        new(WritingActionKind.Proofread, "Proofread", "",
            "Fix spelling, grammar, and punctuation. Make no other changes — keep the wording, " +
            "tone, and meaning exactly as they are."),
        new(WritingActionKind.Rewrite, "Rewrite", "",
            "Rewrite this to read more clearly and naturally while preserving its meaning and tone."),
        new(WritingActionKind.Friendly, "Make friendly", "",
            "Rewrite this in a warm, friendly, approachable tone while preserving its meaning."),
        new(WritingActionKind.Professional, "Make professional", "",
            "Rewrite this in a polished, professional tone suitable for work while preserving its meaning."),
        new(WritingActionKind.Concise, "Make concise", "",
            "Rewrite this to be as concise as possible without losing any essential meaning."),
        new(WritingActionKind.Summarize, "Summarize", "",
            "Summarize this in a short paragraph capturing the key information."),
        new(WritingActionKind.KeyPoints, "Key points", "",
            "Distill this into a short bulleted list of the key points, one per line, each starting with \"- \"."),
    ];

    /// <summary>Looks up an action by its kind (throws if the catalog is missing it — a programming error).</summary>
    public static WritingAction For(WritingActionKind kind) => All.First(a => a.Kind == kind);
}
