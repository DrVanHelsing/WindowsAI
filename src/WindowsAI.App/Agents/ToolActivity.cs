using WindowsAI.Core.Tools;

namespace WindowsAI_App.Agents;

/// <summary>
/// A surfaced agent step (a tool the model invoked), shown as a compact line above the reply.
/// <see cref="Links"/> is non-null only for web searches, whose results render as clickable chips.
/// </summary>
public sealed record ToolActivity(string Glyph, string Summary, IReadOnlyList<WebResult>? Links = null);
