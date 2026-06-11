using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using Microsoft.UI.Dispatching;
using WindowsAI.Core.Chat;
using WindowsAI.Core.Context;
using WindowsAI.Core.History;
using WindowsAI.Core.Providers;
using WindowsAI_App.Agents;

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

    /// <summary>Files queued in the composer, sent (then cleared) with the next message.</summary>
    public ObservableCollection<PendingAttachment> PendingAttachments { get; } = new();

    public bool HasPendingAttachments => PendingAttachments.Count > 0;

    /// <summary>Inline composer note (e.g. "this model can't read images"). Empty = hidden.</summary>
    [ObservableProperty]
    public partial string AttachmentNotice { get; set; } = string.Empty;

    public bool HasAttachmentNotice => AttachmentNotice.Length > 0;

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

    /// <summary>Id of the conversation being built — saved to history after each exchange.</summary>
    public string CurrentConversationId => _current.Id;

    public string ModelLabel => _orchestrator.ActiveModelLabel;

    /// <summary>Compact badge text — the active model's <see cref="ModelOption.ShortName"/>.</summary>
    public string ModelShortLabel
    {
        get
        {
            if (SelectedModel is { } selected)
                return selected.ShortName;

            // Before the catalog loads, derive the same way ModelOption.ShortName does.
            var id = _activeModel.Current.ModelId;
            return id[(id.LastIndexOf('/') + 1)..];
        }
    }

    /// <summary>Badge glyph for the model's source: cloud, or a PC for on-device models.</summary>
    public string ModelGlyph =>
        (SelectedModel?.IsLocal ?? _orchestrator.ActiveProvider.IsLocal) ? "" : "";

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

        PendingAttachments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasPendingAttachments));
            SendCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>
    /// Queues picked files for the next message. Documents must be a type we can extract text
    /// from; images additionally require the active model to support vision — anything else is
    /// rejected with an inline notice rather than failing the send later.
    /// </summary>
    public void AddAttachments(IEnumerable<string> paths)
    {
        var notices = new List<string>();
        foreach (var path in paths)
        {
            if (PendingAttachments.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            if (PendingAttachment.IsImagePath(path))
            {
                if (_orchestrator.ActiveProvider.SupportsVision)
                    PendingAttachments.Add(new PendingAttachment(path));
                else
                    notices.Add($"{Path.GetFileName(path)} skipped — {_orchestrator.ActiveProvider.DisplayName} can't read images; switch to a cloud model.");
            }
            else if (FileTextExtractor.CanExtract(path))
            {
                PendingAttachments.Add(new PendingAttachment(path));
            }
            else
            {
                notices.Add($"{Path.GetFileName(path)} skipped — unsupported file type.");
            }
        }

        AttachmentNotice = string.Join('\n', notices);
    }

    [RelayCommand]
    private void RemoveAttachment(PendingAttachment attachment)
        => PendingAttachments.Remove(attachment);

    partial void OnAttachmentNoticeChanged(string value)
        => OnPropertyChanged(nameof(HasAttachmentNotice));

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

        NotifyModelBadgeChanged();
    }

    private void NotifyModelBadgeChanged()
    {
        OnPropertyChanged(nameof(ModelLabel));
        OnPropertyChanged(nameof(ModelShortLabel));
        OnPropertyChanged(nameof(ModelGlyph));
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

        NotifyModelBadgeChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendAsync()
    {
        var text = Input.Trim();
        var attachments = PendingAttachments.ToList();
        if (text.Length == 0 && attachments.Count == 0)
            return;
        if (text.Length == 0)
            text = "See the attached file(s).";

        Input = string.Empty;
        PendingAttachments.Clear();
        AttachmentNotice = string.Empty;

        var userBubble = new ChatMessageViewModel("You", text, isUser: true);
        if (attachments.Count > 0)
        {
            userBubble.Sources = attachments
                .Select(a => new LocalDocument(a.Path, a.Name, snippet: string.Empty))
                .ToList();
        }
        Messages.Add(userBubble);

        var reply = new ChatMessageViewModel(_orchestrator.ActiveProvider.DisplayName, string.Empty, isUser: false);
        Messages.Add(reply);

        IsBusy = true;
        ChatMessage? toolHint = null;
        ChatMessage? attachmentContext = null;
        try
        {
            attachmentContext = await BuildAttachmentContextAsync(attachments);
            if (attachmentContext is not null)
                _conversation.Add(attachmentContext);

            // Local-first RAG, agentic: when "Search my PC" is on the model is given tools to
            // search the index and open found files, and decides when to use them. The tool
            // delegates drive IsSearchingFiles + the reply's source chips. Tools are built per
            // send so the right view-model instance (shell vs overlay) is updated.
            IReadOnlyList<AITool>? tools = null;
            if (UseLocalContext)
            {
                var dispatcher = DispatcherQueue.GetForCurrentThread();
                tools = AgentToolFactory.CreateLocalTools(
                    _localContext,
                    dispatcher,
                    searching => IsSearchingFiles = searching,
                    docs => AppendSources(reply, docs));

                // Transient guidance so even small models reach for the tools. Removed after
                // the exchange so it never bloats later turns or the saved transcript.
                toolHint = new ChatMessage(ChatRole.System,
                    "You can call tools to access this PC. When the user asks about their own files " +
                    "or local content, call search_my_pc first. Open a file with open_path only when " +
                    "the user asks you to open something, and cite file names you used.");
                _conversation.Add(toolHint);
            }

            _conversation.Add(BuildUserMessage(text, attachments));

            var sb = new StringBuilder();
            await foreach (var delta in _orchestrator.StreamReplyAsync(_conversation, tools))
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
            if (toolHint is not null)
                _conversation.Remove(toolHint);
            // Attachment text is transient like the tool hint: the assistant's reply already
            // reflects it, so keeping it would bloat every later turn with the full document.
            if (attachmentContext is not null)
                _conversation.Remove(attachmentContext);
            IsSearchingFiles = false;
            IsBusy = false;
        }

        await PersistAsync();
    }

    private bool CanSend() => !IsBusy;

    /// <summary>Per-attachment character cap for injected document text — keeps a big PDF from blowing the context window.</summary>
    private const int MaxAttachmentChars = 20_000;

    /// <summary>
    /// Extracts text from document attachments into one transient system message. Extraction
    /// failures become part of the context (the model can tell the user) instead of throwing.
    /// </summary>
    private static async Task<ChatMessage?> BuildAttachmentContextAsync(IReadOnlyList<PendingAttachment> attachments)
    {
        var documents = attachments.Where(a => !a.IsImage).ToList();
        if (documents.Count == 0)
            return null;

        var sb = new StringBuilder("The user attached the following file(s) to their message:\n");
        foreach (var doc in documents)
        {
            string body;
            try
            {
                body = (await FileTextExtractor.ExtractAsync(doc.Path)).Trim();
                if (body.Length > MaxAttachmentChars)
                    body = body[..MaxAttachmentChars] + "\n[…truncated]";
            }
            catch (Exception ex)
            {
                body = $"[could not read file: {ex.Message}]";
            }

            sb.Append("\n--- ").Append(doc.Name).Append(" ---\n").Append(body).Append('\n');
        }

        return new ChatMessage(ChatRole.System, sb.ToString());
    }

    /// <summary>The user turn: plain text, plus inline image bytes when images are attached.</summary>
    private static ChatMessage BuildUserMessage(string text, IReadOnlyList<PendingAttachment> attachments)
    {
        var images = attachments.Where(a => a.IsImage).ToList();
        if (images.Count == 0)
            return new ChatMessage(ChatRole.User, text);

        var contents = new List<AIContent> { new TextContent(text) };
        foreach (var image in images)
        {
            try
            {
                contents.Add(new DataContent(File.ReadAllBytes(image.Path), image.MediaType!));
            }
            catch (Exception ex)
            {
                contents.Add(new TextContent($"[attached image {image.Name} could not be read: {ex.Message}]"));
            }
        }

        return new ChatMessage(ChatRole.User, contents);
    }

    /// <summary>Merges newly-found files into a reply's source chips, de-duped by path.</summary>
    private static void AppendSources(ChatMessageViewModel reply, IReadOnlyList<LocalDocument> documents)
    {
        var merged = new List<LocalDocument>(reply.Sources ?? Array.Empty<LocalDocument>());
        foreach (var doc in documents)
        {
            if (!merged.Any(m => string.Equals(m.Path, doc.Path, StringComparison.OrdinalIgnoreCase)))
                merged.Add(doc);
        }

        reply.Sources = merged;
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
