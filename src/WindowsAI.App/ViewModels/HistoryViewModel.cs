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

    [ObservableProperty]
    public partial bool IsEmpty { get; set; } = true;

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

        IsEmpty = Conversations.Count == 0;
    }

    /// <summary>Loads the given conversation into the shared chat surface.</summary>
    public Task OpenAsync(string id) => _chat.LoadConversationAsync(id);

    public async Task DeleteAsync(string id)
    {
        await _store.DeleteAsync(id);
        await RefreshAsync();
    }
}

/// <summary>A row in the history list: id plus display strings.</summary>
public sealed class ConversationSummary
{
    public ConversationSummary(StoredConversation convo)
    {
        Id = convo.Id;
        Title = string.IsNullOrWhiteSpace(convo.Title) ? "Untitled conversation" : convo.Title;

        var count = convo.Messages.Count;
        Meta = $"{convo.UpdatedAt.LocalDateTime:g}  ·  {count} message{(count == 1 ? "" : "s")}";
    }

    public string Id { get; }

    public string Title { get; }

    public string Meta { get; }
}
