namespace WindowsAI.Core.Providers;

/// <summary>A selectable model in the UI: which provider serves it and its model id.</summary>
public sealed record ModelOption(string ProviderId, string ModelId, string DisplayName, bool IsLocal)
{
    /// <summary>Stable key used to match the current selection in pickers.</summary>
    public string Key => $"{ProviderId}/{ModelId}";

    /// <summary>Short label distinguishing on-device models from cloud ones.</summary>
    public string Kind => IsLocal ? "Local" : "Cloud";
}
