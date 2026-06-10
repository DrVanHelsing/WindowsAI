namespace WindowsAI.Core.Context;

/// <summary>A local file surfaced as chat context by a retriever.</summary>
/// <remarks>
/// Plain class with get-only properties (not a record): types reachable from x:Bind
/// item templates must not look settable to the WinUI XamlTypeInfo generator.
/// </remarks>
public sealed class LocalDocument
{
    public LocalDocument(string path, string name, string snippet)
    {
        Path = path;
        Name = name;
        Snippet = snippet;
    }

    /// <summary>Full file-system path.</summary>
    public string Path { get; }

    /// <summary>File name shown on source chips.</summary>
    public string Name { get; }

    /// <summary>Short content excerpt fed to the model.</summary>
    public string Snippet { get; }
}
