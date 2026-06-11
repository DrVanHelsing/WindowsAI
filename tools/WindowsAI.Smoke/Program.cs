using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using WindowsAI.Core;
using WindowsAI.Core.Chat;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Context;
using WindowsAI.Core.History;
using WindowsAI.Core.Providers;
using WindowsAI.Core.Security;

// Headless smoke test for the platform-agnostic Core. Exercises the real persistence,
// active-model, catalog, and streaming code paths the UI depends on. UI/overlay/hotkey/tray
// must be checked manually (see docs/ROADMAP.md). Exits non-zero if any check fails.

const string LocalModel = "gemma4:e2b-it-qat";

int failures = 0;
void Check(string name, bool ok, string? detail = null)
{
    Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {name}{(detail is null ? "" : $"  ({detail})")}");
    if (!ok) failures++;
}

var tmp = Path.Combine(Path.GetTempPath(), "windowsai-smoke", Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(tmp);

try
{
    // 1) Settings store round-trips to disk.
    Console.WriteLine("Settings store");
    {
        var path = Path.Combine(tmp, "settings.json");
        var store = new JsonSettingsStore(path);
        await store.LoadAsync();                       // missing file -> defaults
        store.Current.ActiveModel.ModelId = "sentinel-model";
        await store.SaveAsync();

        var reloaded = new JsonSettingsStore(path);
        await reloaded.LoadAsync();
        Check("save + reload persists ActiveModel", reloaded.Current.ActiveModel.ModelId == "sentinel-model");
    }

    // 2) Conversation history: save / list (ordered) / get / delete.
    Console.WriteLine("Conversation history");
    {
        var store = new JsonConversationStore(Path.Combine(tmp, "conversations"));
        var older = new StoredConversation { Title = "older", UpdatedAt = DateTimeOffset.Now.AddMinutes(-5) };
        older.Messages.Add(new StoredMessage { Author = "You", Text = "hello", IsUser = true });
        var newer = new StoredConversation { Title = "newer", UpdatedAt = DateTimeOffset.Now };

        await store.SaveAsync(older);
        await store.SaveAsync(newer);

        var list = await store.ListAsync();
        Check("two conversations listed", list.Count == 2, $"count={list.Count}");
        Check("most-recent-first ordering", list.Count == 2 && list[0].Id == newer.Id);

        var fetched = await store.GetAsync(older.Id);
        Check("get returns full message body",
            fetched is { Messages.Count: 1 } && fetched.Messages[0].Text == "hello");

        await store.DeleteAsync(older.Id);
        var afterDelete = await store.ListAsync();
        Check("delete removes one", afterDelete.Count == 1 && afterDelete[0].Id == newer.Id);
    }

    // 3) Active-model service: persists, raises Changed, no-ops on unchanged value.
    Console.WriteLine("Active-model service");
    {
        var store = new JsonSettingsStore(Path.Combine(tmp, "active.json"));
        await store.LoadAsync();
        var svc = new ActiveModelService(store);

        var events = 0;
        svc.Changed += (_, _) => events++;

        await svc.SetAsync("ollama", "model-x");
        Check("Changed fired on real change", events == 1, $"events={events}");
        Check("Current reflects new selection",
            svc.Current is { ProviderId: "ollama", ModelId: "model-x" });

        await svc.SetAsync("ollama", "model-x");            // identical -> no-op
        Check("no event on unchanged value", events == 1, $"events={events}");

        await svc.SetAsync("gemini", "model-y");
        Check("Changed fired again on second change", events == 2, $"events={events}");
    }

    // 3b) File text extraction (chat attachments): plain text passthrough + docx unzip/strip.
    //     PDF is exercised only via CanExtract — hand-crafting a valid PDF here isn't worth it.
    Console.WriteLine("File text extraction (attachments)");
    {
        var txtPath = Path.Combine(tmp, "note.txt");
        await File.WriteAllTextAsync(txtPath, "alpha beta");
        Check("txt reads through", (await FileTextExtractor.ExtractAsync(txtPath)).Trim() == "alpha beta");

        // Minimal .docx: a zip whose word/document.xml carries two paragraphs.
        var docxPath = Path.Combine(tmp, "doc.docx");
        using (var zip = System.IO.Compression.ZipFile.Open(docxPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            writer.Write("<w:document><w:body><w:p><w:r><w:t>first</w:t></w:r></w:p>" +
                         "<w:p><w:r><w:t>second &amp; more</w:t></w:r></w:p></w:body></w:document>");
        }

        var docxText = await FileTextExtractor.ExtractAsync(docxPath);
        Check("docx paragraphs extracted on separate lines",
            docxText.Contains("first\n") && docxText.Contains("second"), $"text=\"{Truncate(docxText.Replace("\n", "\\n"), 60)}\"");
        Check("docx entities decoded", docxText.Contains("second & more"));

        Check("CanExtract accepts pdf/docx/txt",
            FileTextExtractor.CanExtract("a.pdf") && FileTextExtractor.CanExtract("a.DOCX") && FileTextExtractor.CanExtract("a.txt"));
        Check("CanExtract rejects images/binaries",
            !FileTextExtractor.CanExtract("a.png") && !FileTextExtractor.CanExtract("a.exe"));
    }

    // 4) Local context retrieval against the live Windows Search index.
    Console.WriteLine("Local context retrieval (Windows Search index)");
    {
        var retriever = new WindowsAI.Services.Search.WindowsSearchRetriever();
        var docs = await retriever.SearchAsync("windows", maxResults: 3);
        Check("SYSTEMINDEX query returns local documents", docs.Count > 0, $"count={docs.Count}");
        foreach (var doc in docs)
            Console.WriteLine($"         · {doc.Name}");
    }

    // 4b) Embedding re-rank decorator over the keyword retriever.
    Console.WriteLine("Embedding re-rank (Ollama nomic-embed-text)");
    {
        var store = new JsonSettingsStore(Path.Combine(tmp, "embed.json"));
        await store.LoadAsync();
        var embeddings = new OllamaEmbeddingClient(store);

        var vectors = await embeddings.EmbedAsync(new[] { "alpha", "beta" });
        if (vectors is null)
            Console.WriteLine("         · embeddings unavailable (pull nomic-embed-text) — reranker falls back to keywords");
        else
            Check("embedding client returns aligned vectors",
                vectors.Count == 2 && vectors[0].Length > 0, $"dim={vectors[0].Length}");

        var reranker = new RerankingContextRetriever(
            new WindowsAI.Services.Search.WindowsSearchRetriever(), embeddings);
        var ranked = await reranker.SearchAsync("windows", maxResults: 3);
        Check("reranking retriever returns results (re-ranked or fallback)", ranked.Count > 0, $"count={ranked.Count}");
    }

    // 5) + 6) Live paths through DI: catalog discovery + local streaming.
    Console.WriteLine("Catalog + local streaming (requires Ollama running)");
    {
        var services = new ServiceCollection();
        services.AddWindowsAiCore();
        services.AddSingleton<ISecretStore, InMemorySecretStore>();
        var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<ISettingsStore>().LoadAsync();

        var models = await sp.GetRequiredService<IModelCatalog>().GetModelsAsync();
        var localCount = models.Count(m => m.IsLocal);
        Check("catalog lists a local model", localCount > 0, $"local={localCount}, total={models.Count}");
        Check("catalog lists cloud models", models.Any(m => !m.IsLocal));

        // Point the active model at the local one (in-memory only) and stream a reply.
        var settings = sp.GetRequiredService<ISettingsStore>();
        settings.Current.ActiveModel.ProviderId = "ollama";
        settings.Current.ActiveModel.ModelId = LocalModel;

        var conversation = new List<ChatMessage>
        {
            new(ChatRole.System, "You are a test harness. Answer with the single word: OK"),
            new(ChatRole.User, "Respond now."),
        };

        var sb = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try
        {
            await foreach (var delta in sp.GetRequiredService<ChatOrchestrator>().StreamReplyAsync(conversation, tools: null, cts.Token))
                sb.Append(delta);

            var reply = sb.ToString().Trim();
            Check("local model streamed a non-empty reply", reply.Length > 0, $"reply=\"{Truncate(reply, 80)}\"");
            Check("reply has no leaked <think> tags", !reply.Contains("<think>", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Check("local model streamed a non-empty reply", false, $"exception: {ex.Message}");
        }
    }

    // 7) Agent tool-calling loop (provider-independent: scripted stub client + real tool).
    //    Verifies the same FunctionInvokingChatClient wiring the orchestrator uses: the model
    //    requests a tool, it is invoked with the right args, the result is fed back, and a
    //    final text reply is produced.
    Console.WriteLine("Agent tool-calling loop (scripted)");
    {
        var toolInvoked = false;
        string? receivedValue = null;

        var echo = AIFunctionFactory.Create(
            (string value) => { toolInvoked = true; receivedValue = value; return $"echoed:{value}"; },
            name: "test_echo",
            description: "Echoes the input value.");

        var client = new ScriptedToolClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        var options = new ChatOptions { Tools = new List<AITool> { echo } };
        var messages = new List<ChatMessage> { new(ChatRole.User, "Use the tool.") };

        var sb = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await foreach (var update in client.GetStreamingResponseAsync(messages, options, cts.Token))
                if (!string.IsNullOrEmpty(update.Text))
                    sb.Append(update.Text);

            var final = sb.ToString();
            Check("tool was invoked by the loop", toolInvoked, $"value=\"{receivedValue}\"");
            Check("tool received the model's argument", receivedValue == "ping", $"value=\"{receivedValue}\"");
            Check("final reply reflects the tool result", final.Contains("echoed:ping", StringComparison.OrdinalIgnoreCase),
                $"reply=\"{Truncate(final, 80)}\"");
        }
        catch (Exception ex)
        {
            Check("agent tool-calling loop", false, $"exception: {ex.Message}");
        }
    }
}
finally
{
    try { Directory.Delete(tmp, recursive: true); } catch { /* best effort */ }
}

Console.WriteLine();
Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) FAILED.");
return failures == 0 ? 0 : 1;

static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

/// <summary>Throwaway in-memory secret store so DI can build without the Windows DPAPI store.</summary>
file sealed class InMemorySecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _store = new();
    public void Set(string key, string secret) => _store[key] = secret;
    public string? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
    public void Delete(string key) => _store.Remove(key);
}

/// <summary>
/// A deterministic <see cref="IChatClient"/> for testing the tool-calling loop without a real
/// model: the first turn (no tool results yet) asks to call <c>test_echo("ping")</c>; once the
/// result comes back it returns a final text reply that includes the result.
/// </summary>
file sealed class ScriptedToolClient : IChatClient
{
    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var contents = Script(messages).SelectMany(u => u.Contents).ToList();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, contents)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var update in Script(messages))
        {
            await Task.Yield();
            yield return update;
        }
    }

    private static IEnumerable<ChatResponseUpdate> Script(IEnumerable<ChatMessage> messages)
    {
        var result = messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionResultContent>()
            .LastOrDefault();

        if (result is null)
        {
            var call = new FunctionCallContent(
                callId: "call-1",
                name: "test_echo",
                arguments: new Dictionary<string, object?> { ["value"] = "ping" });
            yield return new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent> { call });
        }
        else
        {
            yield return new ChatResponseUpdate(ChatRole.Assistant, $"Final: {result.Result}");
        }
    }
}
