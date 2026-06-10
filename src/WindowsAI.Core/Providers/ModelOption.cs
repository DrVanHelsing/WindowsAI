namespace WindowsAI.Core.Providers;

/// <summary>A selectable model in the UI: which provider serves it and its model id.</summary>
/// <remarks>
/// A plain class (not a record) with get-only properties on purpose: the WinUI
/// XamlTypeInfo generator emits setter calls for record properties when the type is
/// reachable from a XAML-visible collection type, which fails compilation (CS0200/CS8852).
/// </remarks>
public sealed class ModelOption
{
    public ModelOption(string providerId, string modelId, string displayName, bool isLocal)
    {
        ProviderId = providerId;
        ModelId = modelId;
        DisplayName = displayName;
        IsLocal = isLocal;
    }

    public string ProviderId { get; }

    public string ModelId { get; }

    public string DisplayName { get; }

    public bool IsLocal { get; }

    /// <summary>Stable key used to match the current selection in pickers.</summary>
    public string Key => $"{ProviderId}/{ModelId}";

    /// <summary>Short label distinguishing on-device models from cloud ones.</summary>
    public string Kind => IsLocal ? "Local" : "Cloud";
}
