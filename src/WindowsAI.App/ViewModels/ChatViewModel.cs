using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using WindowsAI.Core.Chat;
using WindowsAI.Core.Context;
using WindowsAI.Core.History;
using WindowsAI.Core.Providers;

namespace WindowsAI_App.ViewModels;

/// <summary>
/// Drives the chat surface: holds the visible bubbles and the underlying model
/// conversation, streams replies, and lets the user switch the active model.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private const string SystemPrompt =
        "You are WindowsAI, a concise and helpful on-device assistant for Windows. " +
        "Prefer short, direct answers.";

    private readonly ChatOrchestrator _orchestrator;
    private readonly IModelCatalog _catalog;
    private readonly ActiveModelService _activeModel;
    private readonly IConversationStore _history;
    private readonly ILocalContextRetriever _localContext;
    private readonly List<ChatMessage> _conversation = new();

    private StoredConversation _current = new();
    private bool _applyingSelection;

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();

    public ObservableCollection<ModelOption> AvailableModels { get; } = new();

    [ObservableProperty]
    public partial ModelOption? SelectedModel { get; set; }

    [ObservableProperty]
    public partial string Input { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>When on, each prompt is augmented with matching files from this PC (local RAG).</summary>
    [ObservableProperty]
    public partial bool UseLocalContext { get; set; }

    /// <summary>True only while a local file search is running — drives the sparkle indicator.</summary>
    [ObservableProperty]
    public partial bool IsSearchingFiles { get; set; }

    public bool IsNotBusy => !IsBusy;

    public string ModelLabel => _orchestrator.ActiveModelLabel;

    public ChatViewModel(
        ChatOrchestrator orchestrator,
        IModelCatalog catalog,
        ActiveModelService activeModel,
        IConversationStore history,
        ILocalContextRetriever localContext)
    {
        _orchestrator = orchestrator;
        _catalog = catalog;
        _activeModel = activeModel;
        _history = history;
        _localContext = localContext;
        _conversation.Add(new ChatMessage(ChatRole.System, SystemPrompt));

        // Stay in sync when the active model is changed on any other surface.
        _activeModel.Changed += OnActiveModelChanged;
    }

    /// <summary>Loads the model list and selects the active one. Call after construction.</summary>
    public async Task InitializeAsync()
    {
        var models = await _catalog.GetModelsAsync();

        _applyingSelection = true;
        AvailableModels.Clear();
        foreach (var m in models)
            AvailableModels.Add(m);

        var active = _activeModel.Current;
        SelectedModel =
            AvailableModels.FirstOrDefault(m => m.ProviderId == active.ProviderId && m.ModelId == active.ModelId)
            ?? AvailableModels.FirstOrDefault();
        _applyingSelection = false;

        // If settings pointed at a model that no longer exists, adopt the fallback everywhere.
        if (SelectedModel is { } fallback &&
            (fallback.ProviderId != active.ProviderId || fallback.ModelId != active.ModelId))
        {
            await _activeModel.SetAsync(fallback.ProviderId, fallback.ModelId);
        }

        OnPropertyChanged(nameof(ModelLabel));
    }

    partial void OnSelectedModelChanged(ModelOption? value)
    {
        if (_applyingSelection || value is null)
            return;

        _ = _activeModel.SetAsync(value.ProviderId, value.ModelId);
    }

    /// <summary>Another surface changed the active model — mirror it into this view model.</summary>
    private void OnActiveModelChanged(object? sender, EventArgs e)
    {
        var active = _activeModel.Current;

        _applyingSelection = true;
        SelectedModel =
            AvailableModels.FirstOrDefault(m => m.ProviderId == active.ProviderId && m.ModelId == active.ModelId)
            ?? SelectedModel;
        _applyingSelection = false;

        OnPropertyChanged(nameof(ModelLabel));
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        if (text.Length == 0)
            return;

        Input = string.Empty;

        Messages.Add(new ChatMessageViewModel("You", text, isUser: true));

        var reply = new ChatMessageViewModel(_orchestrator.ActiveProvider.DisplayName, string.Empty, isUser: false);
        Messages.Add(reply);

        IsBusy = true;
        ChatMessage? contextMessage = null;
        try
        {
            // Local-first RAG: surface matching files from this PC as a transient
            // system message. Removed after the exchange so it never bloats later turns.
            if (UseLocalContext)
            {
                IsSearchingFiles = true;
                IReadOnlyList<LocalDocument> documents;
                try
                {
                    documents = await _localContext.SearchAsync(text, maxResults: 4);
                }
                finally
                {
                    IsSearchingFiles = false;
                }

                if (documents.Count > 0)
                {
                    contextMessage = new ChatMessage(ChatRole.System, BuildContextBlock(documents));
                    _conversation.Add(contextMessage);
                    reply.Sources = documents;
                }
            }

            _conversation.Add(new ChatMessage(ChatRole.User, text));

            var sb = new StringBuilder();
            await foreach (var delta in _orchestrator.StreamReplyAsync(_conversation))
            {
                sb.Append(delta);
                reply.Text = sb.ToString();
            }

            reply.Text = sb.Length == 0 ? "(no response)" : sb.ToString();
            _conversation.Add(new ChatMessage(ChatRole.Assistant, reply.Text));
        }
        catch (Exception ex)
        {
            reply.Text = $"⚠ {ex.Message}";
        }
        finally
        {
            if (contextMessage is not null)
                _conversation.Remove(contextMessage);
            IsBusy = false;
        }

        await PersistAsync();
    }

    private bool CanSend() => !IsBusy;

    /// <summary>Formats retrieved files as a context block the model can cite from.</summary>
    private static string BuildContextBlock(IReadOnlyList<LocalDocument> documents)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            "The user enabled local file search. The following files from their PC may be " +
            "relevant to the next question. Use them when helpful and cite file names.");

        for (int i = 0; i < documents.Count; i++)
        {
            sb.AppendLine();
            sb.AppendLine($"[{i + 1}] {documents[i].Path}");
            sb.AppendLine(documents[i].Snippet);
        }

        return sb.ToString();
    }

    /// <summary>Starts a fresh conversation, clearing the screen and the model context.</summary>
    [RelayCommand]
    private void NewConversation()
    {
        Messages.Clear();
        _conversation.Clear();
        _conversation.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        _current = new StoredConversation();
    }

    /// <summary>Replaces the current session with a saved one loaded from history.</summary>
    public async Task LoadConversationAsync(string id)
    {
        var convo = await _history.GetAsync(id);
        if (convo is null)
            return;

        _current = convo;

        Messages.Clear();
        _conversation.Clear();
        _conversation.Add(new ChatMessage(ChatRole.System, SystemPrompt));

        foreach (var m in convo.Messages)
        {
            Messages.Add(new ChatMessageViewModel(m.Author, m.Text, m.IsUser));
            _conversation.Add(new ChatMessage(m.IsUser ? ChatRole.User : ChatRole.Assistant, m.Text));
        }
    }

    /// <summary>Snapshots the visible conversation to the store after each completed exchange.</summary>
    private async Task PersistAsync()
    {
        _current.Messages = Messages
            .Select(m => new StoredMessage { Author = m.Author, Text = m.Text, IsUser = m.IsUser })
            .ToList();

        if (string.IsNullOrWhiteSpace(_current.Title))
            _current.Title = DeriveTitle();

        _current.UpdatedAt = DateTimeOffset.Now;
        await _history.SaveAsync(_current);
    }

    /// <summary>Title from the first user message, trimmed to a single short line.</summary>
    private string DeriveTitle()
    {
        var first = Messages.FirstOrDefault(m => m.IsUser)?.Text.Trim().ReplaceLineEndings(" ");
        if (string.IsNullOrWhiteSpace(first))
            return "New conversation";

        return first.Length <= 60 ? first : first[..60].TrimEnd() + "…";
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsNotBusy));
        SendCommand.NotifyCanExecuteChanged();
    }
}
