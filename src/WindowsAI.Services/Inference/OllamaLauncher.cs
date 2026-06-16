using System.Diagnostics;
using System.Net.Http;
using WindowsAI.Core.Configuration;

namespace WindowsAI.Services.Inference;

/// <summary>
/// Ensures Ollama is running before the first inference request.
/// If the configured endpoint is not responding, tries to locate and start ollama.exe,
/// then polls until it is ready (up to <see cref="ReadyTimeoutMs"/> ms).
/// </summary>
public static class OllamaLauncher
{
    // These are the standard Windows install paths for Ollama (user-scoped installer).
    private static readonly string[] CandidatePaths =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     "Programs", "Ollama", "ollama.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     "Ollama", "ollama.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     "Ollama", "ollama.exe"),
    ];

    private const int ProbeTimeoutMs  = 1_500;
    private const int ReadyTimeoutMs  = 20_000;
    private const int PollIntervalMs  = 500;

    /// <summary>
    /// Ensures Ollama is listening on the endpoint stored in <paramref name="settings"/>.
    /// Fire-and-forget safe: swallows all exceptions so a missing Ollama install never
    /// crashes the app on startup.
    /// </summary>
    public static async Task EnsureRunningAsync(ISettingsStore settings)
    {
        // Use the endpoint of the first enabled Ollama connection (falls back to the default).
        var endpoint = settings.Current.Providers
            .FirstOrDefault(p => p.Enabled && p.Kind == ProviderKind.Ollama)?.BaseUrl
            ?? "http://localhost:11434";
        var probeUrl = endpoint.TrimEnd('/') + "/api/tags";

        if (await IsRespondingAsync(probeUrl))
            return;

        // Not running — try to start it.
        var exePath = FindOllamaExe();
        if (exePath is null)
            return; // Ollama not installed; give up silently.

        try
        {
            // "ollama serve" is the explicit server command; running ollama.exe
            // without arguments on Windows also starts the background server.
            var psi = new ProcessStartInfo(exePath, "serve")
            {
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = false,
                RedirectStandardError  = false,
            };
            Process.Start(psi);
        }
        catch
        {
            return;
        }

        // Poll until ready.
        var deadline = Environment.TickCount64 + ReadyTimeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            await Task.Delay(PollIntervalMs);
            if (await IsRespondingAsync(probeUrl))
                return;
        }
    }

    private static async Task<bool> IsRespondingAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMilliseconds(ProbeTimeoutMs) };
            var resp = await http.GetAsync(url);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindOllamaExe()
    {
        // 1. PATH lookup (covers Scoop / winget / manual installs).
        var fromPath = FindOnPath("ollama.exe");
        if (fromPath is not null) return fromPath;

        // 2. Known install locations.
        foreach (var path in CandidatePaths)
            if (File.Exists(path)) return path;

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(dir.Trim(), fileName);
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
