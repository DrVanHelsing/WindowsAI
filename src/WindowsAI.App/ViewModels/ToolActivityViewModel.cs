using WindowsAI.Core.Tools;
using WindowsAI_App.Agents;

namespace WindowsAI_App.ViewModels;

/// <summary>A surfaced agent step in a reply (glyph + summary, with optional web-result links).</summary>
public sealed class ToolActivityViewModel
{
    public ToolActivityViewModel(ToolActivity activity)
    {
        Glyph = activity.Glyph;
        Summary = activity.Summary;
        Links = activity.Links ?? Array.Empty<WebResult>();
    }

    public string Glyph { get; }

    public string Summary { get; }

    public IReadOnlyList<WebResult> Links { get; }

    public bool HasLinks => Links.Count > 0;
}
