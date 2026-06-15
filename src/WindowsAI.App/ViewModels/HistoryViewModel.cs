using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowsAI.Core.History;

namespace WindowsAI_App.ViewModels;

/// <summary>
/// Backs the History page: lists saved conversations, opens one into the shared chat,
/// and deletes them. Reads through <see cref="IConversationStore"/> and loads into the
/// shell's <see cref="ChatViewModel"/> singleton.
/// </summary>
public partial class HistoryViewModel : ObservableObject
{
    private readonly IConversationStore _store;
    private readonly ChatViewModel _chat;

    public ObservableCollection<ConversationSummary> Conversations { get; } = new();

    /// <summary>Conversations bucketed by recency for the grouped history list.</summary>
    public ObservableCollection<ConversationGroup> Groups { get; } = new();

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

    /// <summary>Live filter over title + snippet. Empty shows everything.</summary>
    [ObservableProperty]
    public partial string SearchText { get; set; } = string.Empty;

    public HistoryViewModel(IConversationStore store, ChatViewModel chat)
    {
        _store = store;
        _chat = chat;
    }

    /// <summary>Reloads the list from disk. Call whenever the page is shown.</summary>
    public async Task RefreshAsync()
    {
        var saved = await _store.ListAsync();

        Conversations.Clear();
        foreach (var convo in saved)
            Conversations.Add(new ConversationSummary(convo));

        RebuildGroups();
    }

    partial void OnSearchTextChanged(string value) => RebuildGroups();

    /// <summary>
    /// Rebuilds the grouped view from the cached <see cref="Conversations"/>, applying the current
    /// search filter. Pinned conversations form a leading "Pinned" group; the rest keep recency
    /// buckets. The source list is already ordered (pinned first, then recency) so group order follows.
    /// </summary>
    private void RebuildGroups()
    {
        var query = SearchText?.Trim() ?? string.Empty;
        IEnumerable<ConversationSummary> visible = Conversations;
        if (query.Length > 0)
        {
            visible = Conversations.Where(c =>
                c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                c.Snippet.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        Groups.Clear();
        foreach (var bucket in visible.GroupBy(c => c.GroupKey))
        {
            var group = new ConversationGroup(bucket.Key);
            group.AddRange(bucket);
            Groups.Add(group);
        }

        // Empty state reflects the underlying list, not the filtered view.
        IsEmpty = Conversations.Count == 0;
    }

    /// <summary>Loads the given conversation into the shared chat surface.</summary>
    public Task OpenAsync(string id) => _chat.LoadConversationAsync(id);

    public async Task DeleteAsync(string id)
    {
        await _store.DeleteAsync(id);
        await RefreshAsync();
    }

    /// <summary>Gives a saved conversation a new title. No-op if it no longer exists.</summary>
    public async Task RenameAsync(string id, string newTitle)
    {
        var convo = await _store.GetAsync(id);
        if (convo is null)
            return;

        convo.Title = newTitle;
        await _store.SaveAsync(convo);
        await RefreshAsync();
    }

    /// <summary>Pins or unpins a conversation, then reloads so it floats to / leaves the Pinned group.</summary>
    public async Task PinAsync(string id, bool pinned)
    {
        var convo = await _store.GetAsync(id);
        if (convo is null)
            return;

        convo.IsPinned = pinned;
        await _store.SaveAsync(convo);
        await RefreshAsync();
    }
}

/// <summary>A recency bucket of conversations ("Today", "Yesterday", …).</summary>
public sealed class ConversationGroup : List<ConversationSummary>
{
    public ConversationGroup(string key) => Key = key;

    public string Key { get; }
}

/// <summary>A row in the history list: id plus display strings.</summary>
public sealed class ConversationSummary
{
    public ConversationSummary(StoredConversation convo)
    {
        Id = convo.Id;
        Title = string.IsNullOrWhiteSpace(convo.Title) ? "Untitled conversation" : convo.Title;
        IsPinned = convo.IsPinned;

        var count = convo.Messages.Count;
        var when = convo.UpdatedAt.LocalDateTime;
        Meta = $"{FriendlyTime(when)}  ·  {count} message{(count == 1 ? "" : "s")}";
        // Pinned conversations live in a single leading group instead of their recency bucket.
        GroupKey = convo.IsPinned ? "Pinned" : BucketOf(when);

        var last = convo.Messages.LastOrDefault()?.Text.ReplaceLineEndings(" ").Trim() ?? string.Empty;
        Snippet = last.Length <= 90 ? last : last[..90].TrimEnd() + "…";
    }

    public string Id { get; }

    public string Title { get; }

    public string Meta { get; }

    /// <summary>Whether this conversation is pinned — drives the pin/unpin button state.</summary>
    public bool IsPinned { get; }

    /// <summary>Pin glyph: filled "unpin" when pinned, outline "pin" otherwise.</summary>
    public string PinGlyph => IsPinned ? "" : "";

    public string PinTooltip => IsPinned ? "Unpin" : "Pin";

    /// <summary>Single-line preview of the last message.</summary>
    public string Snippet { get; }

    /// <summary>Recency bucket (or "Pinned") used to group the history list.</summary>
    public string GroupKey { get; }

    private static string FriendlyTime(DateTime when)
    {
        var today = DateTime.Today;
        if (when.Date == today) return $"Today · {when:HH:mm}";
        if (when.Date == today.AddDays(-1)) return $"Yesterday · {when:HH:mm}";
        if (when.Date > today.AddDays(-7)) return $"{when:dddd} · {when:HH:mm}";
        return when.ToString("d MMM yyyy");
    }

    private static string BucketOf(DateTime when)
    {
        var today = DateTime.Today;
        if (when.Date == today) return "Today";
        if (when.Date == today.AddDays(-1)) return "Yesterday";
        if (when.Date > today.AddDays(-7)) return "This week";
        return "Earlier";
    }
}
