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
using WindowsAI.Core.Tools;
using WindowsAI_App.Agents;

namespace WindowsAI_App.ViewModels;

/// <summary>
/// Drives the chat surface: holds the visible bubbles and the underlying model
/// conversation, streams replies, and lets the user switch the active model.
/// </summary>
public partial class ChatViewModel : ObservableObject
{
    private const string DefaultSystemPrompt =
        "You are WindowsAI, a concise and helpful on-device assistant for Windows. " +
        "Prefer short, direct answers.";

    /// <summary>The system prompt in effect: a per-conversation override if set, else the default.</summary>
    private string EffectiveSystemPrompt() =>
        string.IsNullOrWhiteSpace(_current.Overrides?.SystemPrompt) ? DefaultSystemPrompt : _current.Overrides!.SystemPrompt!;

    private readonly ChatOrchestrator _orchestrator;
    private readonly IModelCatalog _catalog;
    private readonly ActiveModelService _activeModel;
    private readonly IConversationStore _history;
    private readonly ILocalContextRetriever _localContext;
    private readonly ImageGenerationService _imageGen;
    private readonly WebSearchService _webSearch;
    private readonly List<ChatMessage> _conversation = new();

    private StoredConversation _current = new();
    private bool _applyingSelection;

    /// <summary>Cancels the in-flight streaming reply when the user hits Stop.</summary>
    private CancellationTokenSource? _cts;

    // The most recent user turn, kept so Retry can regenerate the reply for it.
    private string? _lastUserText;
    private List<PendingAttachment> _lastAttachments = new();

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
        ILocalContextRetriever localContext,
        ImageGenerationService imageGen,
        WebSearchService webSearch)
    {
        _orchestrator = orchestrator;
        _catalog = catalog;
        _activeModel = activeModel;
        _history = history;
        _localContext = localContext;
        _imageGen = imageGen;
        _webSearch = webSearch;
        _conversation.Add(new ChatMessage(ChatRole.System, EffectiveSystemPrompt()));

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

        _lastUserText = text;
        _lastAttachments = attachments;

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

        // Image generation is a distinct path: detected by intent, served by OpenRouter regardless
        // of the selected chat model, and rendered as an inline image rather than a streamed reply.
        if (attachments.Count == 0 && LooksLikeImageRequest(text))
        {
            await GenerateImageAsync(text, reply);
            return;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();
        ChatMessage? toolHint = null;
        ChatMessage? attachmentContext = null;
        try
        {
            attachmentContext = await BuildAttachmentContextAsync(attachments);
            if (attachmentContext is not null)
                _conversation.Add(attachmentContext);

            // Agentic tools, built per send so the right view-model instance (shell vs overlay) is
            // updated: keyless utilities (date/time) always; search_web when configured; local-RAG
            // (search_my_pc/open_path) when "Search my PC" is on. Each tool surfaces a step line via
            // onActivity, and the local ones additionally drive IsSearchingFiles + source chips.
            var dispatcher = DispatcherQueue.GetForCurrentThread();
            void OnActivity(ToolActivity a) => reply.AddActivity(new ToolActivityViewModel(a));

            var toolSet = new List<AITool>(AgentToolFactory.CreateUtilityTools(dispatcher, OnActivity));

            var hints = new List<string>();
            if (_webSearch.IsConfigured)
            {
                toolSet.AddRange(AgentToolFactory.CreateWebTools(_webSearch, dispatcher, OnActivity));
                hints.Add("When the user asks about current events or facts that may have changed, call search_web and cite sources.");
            }

            if (UseLocalContext)
            {
                toolSet.AddRange(AgentToolFactory.CreateLocalTools(
                    _localContext,
                    dispatcher,
                    searching => IsSearchingFiles = searching,
                    docs => AppendSources(reply, docs),
                    OnActivity));
                hints.Add("When the user asks about their own files or local content, call search_my_pc first. " +
                          "Open a file with open_path only when the user asks you to open something, and cite file names you used.");
            }

            IReadOnlyList<AITool>? tools = toolSet;

            if (hints.Count > 0)
            {
                // Transient guidance so even small models reach for the tools. Removed after
                // the exchange so it never bloats later turns or the saved transcript.
                toolHint = new ChatMessage(ChatRole.System,
                    "You can call tools to help answer. " + string.Join(" ", hints));
                _conversation.Add(toolHint);
            }

            _conversation.Add(BuildUserMessage(text, attachments));

            var sb = new StringBuilder();
            await foreach (var delta in _orchestrator.StreamReplyAsync(_conversation, tools, _cts.Token))
            {
                sb.Append(delta);
                reply.Text = sb.ToString();
            }

            reply.Text = sb.Length == 0 ? "(no response)" : sb.ToString();
            _conversation.Add(new ChatMessage(ChatRole.Assistant, reply.Text));
        }
        catch (OperationCanceledException)
        {
            // User hit Stop — keep whatever streamed so far so it's not lost.
            var partial = reply.Text;
            reply.Text = string.IsNullOrEmpty(partial) ? "(stopped)" : partial + " …(stopped)";
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
            _cts?.Dispose();
            _cts = null;
        }

        await PersistAsync();
    }

    private bool CanSend() => !IsBusy;

    /// <summary>Requests cancellation of the in-flight reply — bound to the Stop button while busy.</summary>
    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    /// <summary>Regenerates the reply to the most recent user turn (drops the last exchange and resends).</summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        if (IsBusy || _lastUserText is null)
            return;

        // Drop the trailing assistant + user bubbles and their model messages, then resend.
        if (Messages.Count > 0 && !Messages[^1].IsUser)
            Messages.RemoveAt(Messages.Count - 1);
        if (Messages.Count > 0 && Messages[^1].IsUser)
            Messages.RemoveAt(Messages.Count - 1);
        RemoveLastTurn(ChatRole.Assistant);
        RemoveLastTurn(ChatRole.User);

        Input = _lastUserText;
        PendingAttachments.Clear();
        foreach (var a in _lastAttachments)
            PendingAttachments.Add(a);

        await SendAsync();
    }

    /// <summary>Removes the most recent working-conversation message with the given role.</summary>
    private void RemoveLastTurn(ChatRole role)
    {
        for (int i = _conversation.Count - 1; i >= 0; i--)
        {
            if (_conversation[i].Role == role)
            {
                _conversation.RemoveAt(i);
                return;
            }
        }
    }

    // Matches phrasings like "generate an image of…", "draw a picture of…", "make me a logo of…".
    private static readonly System.Text.RegularExpressions.Regex s_imageRequest = new(
        @"\b(generate|create|make|draw|paint|render|design|produce|sketch|illustrate|imagine)\b.{0,40}\b(image|picture|photo|drawing|illustration|logo|icon|art(work)?|painting|sketch|wallpaper|poster|render)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static bool LooksLikeImageRequest(string text) => s_imageRequest.IsMatch(text);

    /// <summary>Runs the OpenRouter image path and renders the result (or an error) into <paramref name="reply"/>.</summary>
    private async Task GenerateImageAsync(string prompt, ChatMessageViewModel reply)
    {
        IsBusy = true;
        try
        {
            var result = await _imageGen.GenerateAsync(prompt);
            if (result.Success)
            {
                // Markdown image → the native renderer loads the saved file. Keep any caption text above it.
                var caption = string.IsNullOrWhiteSpace(result.Text) ? string.Empty : result.Text!.Trim() + "\n\n";
                reply.Text = $"{caption}![{EscapeAlt(prompt)}]({result.FilePath})";
            }
            else
            {
                reply.Text = $"⚠ {result.Error}";
            }

            _conversation.Add(new ChatMessage(ChatRole.Assistant, reply.Text));
        }
        catch (Exception ex)
        {
            reply.Text = $"⚠ {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }

        await PersistAsync();
    }

    private static string EscapeAlt(string text)
    {
        var alt = text.Length > 80 ? text[..80] : text;
        return alt.Replace("[", string.Empty).Replace("]", string.Empty).Replace("\n", " ");
    }

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
        _current = new StoredConversation();
        _conversation.Add(new ChatMessage(ChatRole.System, EffectiveSystemPrompt()));
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
        _conversation.Add(new ChatMessage(ChatRole.System, EffectiveSystemPrompt()));

        foreach (var m in convo.Messages)
        {
            Messages.Add(new ChatMessageViewModel(m.Author, m.Text, m.IsUser));
            _conversation.Add(new ChatMessage(m.IsUser ? ChatRole.User : ChatRole.Assistant, m.Text));
        }

        // Restore this conversation's pinned model, if it saved one.
        if (_current.Overrides is { ProviderId: { Length: > 0 } providerId, ModelId: { Length: > 0 } modelId })
            await _activeModel.SetAsync(providerId, modelId);
    }

    /// <summary>This conversation's custom system prompt, or empty when it uses the app default.</summary>
    public string CustomSystemPrompt => _current.Overrides?.SystemPrompt ?? string.Empty;

    /// <summary>
    /// Applies per-conversation overrides chosen in the conversation-settings dialog. An empty/default
    /// prompt clears the prompt override; a chosen model is applied immediately and remembered.
    /// </summary>
    public async Task ApplyConversationOverridesAsync(string systemPrompt, ModelOption? model)
    {
        var overrides = _current.Overrides ?? new ConversationOverrides();

        var prompt = systemPrompt.Trim();
        overrides.SystemPrompt =
            (prompt.Length == 0 || prompt == DefaultSystemPrompt) ? null : prompt;

        if (model is not null)
        {
            overrides.ProviderId = model.ProviderId;
            overrides.ModelId = model.ModelId;
            await _activeModel.SetAsync(model.ProviderId, model.ModelId);
        }

        _current.Overrides = overrides.HasAny ? overrides : null;

        // Re-apply the system message in the working conversation (always index 0).
        if (_conversation.Count > 0 && _conversation[0].Role == ChatRole.System)
            _conversation[0] = new ChatMessage(ChatRole.System, EffectiveSystemPrompt());

        OnPropertyChanged(nameof(CustomSystemPrompt));

        // Persist immediately only if this conversation already exists on disk (has messages).
        if (Messages.Count > 0)
            await PersistAsync();
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
