using System.Text;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using WindowsAI.Core;
using WindowsAI.Core.Chat;
using WindowsAI.Core.Configuration;
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

    // 4) + 5) Live paths through DI: catalog discovery + local streaming.
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
            await foreach (var delta in sp.GetRequiredService<ChatOrchestrator>().StreamReplyAsync(conversation, cts.Token))
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
