using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Security;

namespace WindowsAI.Core.Providers;

/// <summary>Outcome of an image-generation request: a saved file path, or a human-readable reason it didn't produce one.</summary>
public sealed record ImageGenerationResult(string? FilePath, string? Text, string? Error)
{
    public bool Success => FilePath is not null;
    public static ImageGenerationResult Ok(string path, string? text) => new(path, text, null);
    public static ImageGenerationResult Fail(string error) => new(null, null, error);
}

/// <summary>
/// Text-to-image via OpenRouter. OpenRouter exposes image generation through the chat-completions
/// protocol with <c>modalities: ["image","text"]</c>; the reply message carries the picture(s) as
/// base64 data URLs. The OpenAI <see cref="System.ClientModel"/> client doesn't surface that field,
/// so this calls the endpoint directly, decodes the image, and saves it under LocalAppData. The
/// returned path is rendered by the chat's native image pipeline.
/// </summary>
public sealed class ImageGenerationService
{
    private static readonly HttpClient s_http = new() { Timeout = TimeSpan.FromSeconds(120) };

    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;

    public ImageGenerationService(ISettingsStore settings, ISecretStore secrets)
    {
        _settings = settings;
        _secrets = secrets;
    }

    /// <summary>True once an OpenRouter API key is present.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_secrets.Get(SecretKeys.OpenRouterApiKey));

    public async Task<ImageGenerationResult> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var key = _secrets.Get(SecretKeys.OpenRouterApiKey);
        if (string.IsNullOrWhiteSpace(key))
            return ImageGenerationResult.Fail("Image generation uses OpenRouter — add an OpenRouter API key in Settings.");

        var options = _settings.Current.OpenRouter;
        var url = options.BaseUrl.TrimEnd('/') + "/chat/completions";

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = JsonContent.Create(new
        {
            model = options.ImageModelId,
            messages = new[] { new { role = "user", content = prompt } },
            modalities = new[] { "image", "text" },
        });

        try
        {
            using var response = await s_http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return ImageGenerationResult.Fail($"OpenRouter image request failed ({(int)response.StatusCode}): {ExtractError(json)}");

            return await ParseAsync(json, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ImageGenerationResult.Fail($"Image generation failed: {ex.Message}");
        }
    }

    private static async Task<ImageGenerationResult> ParseAsync(string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return ImageGenerationResult.Fail("The model returned no image.");

        var message = choices[0].GetProperty("message");
        string? text = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
            ? c.GetString()
            : null;

        if (!message.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array || images.GetArrayLength() == 0)
        {
            // No image came back — surface any explanatory text the model gave instead.
            return ImageGenerationResult.Fail(string.IsNullOrWhiteSpace(text)
                ? "The model didn't return an image. Try a more explicit request like \"draw a picture of …\"."
                : text!);
        }

        var dataUrl = images[0].GetProperty("image_url").GetProperty("url").GetString();
        if (string.IsNullOrWhiteSpace(dataUrl) || !dataUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return ImageGenerationResult.Fail("The model returned an image in an unsupported format.");

        var path = await SaveDataUrlAsync(dataUrl, ct);
        return ImageGenerationResult.Ok(path, text);
    }

    private static async Task<string> SaveDataUrlAsync(string dataUrl, CancellationToken ct)
    {
        // data:[<mime>];base64,<payload>
        var comma = dataUrl.IndexOf(',');
        var header = dataUrl[..comma];
        var payload = dataUrl[(comma + 1)..];
        var bytes = Convert.FromBase64String(payload);

        var ext = header.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ? "jpg"
            : header.Contains("webp", StringComparison.OrdinalIgnoreCase) ? "webp"
            : "png";

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WindowsAI", "images");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, $"img-{DateTime.Now:yyyyMMdd-HHmmss-fff}.{ext}");
        await File.WriteAllBytesAsync(path, bytes, ct);
        return path;
    }

    private static string ExtractError(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.TryGetProperty("message", out var m) ? m.GetString() ?? json : err.ToString();
        }
        catch { /* not JSON — fall through */ }
        return json.Length > 200 ? json[..200] : json;
    }
}
