using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowsAI.Core.Configuration;
using WindowsAI.Core.Providers;
using WindowsAI.Core.Security;
using WindowsAI_App.Services;

namespace WindowsAI_App.ViewModels;

/// <summary>
/// Backs the feature-organized Settings page: appearance, per-capability model routing (Chat,
/// Visual Intelligence, Writing Tools, Image Generation), feature enable toggles, the web-search
/// key, and rebindable hotkeys. Provider/key management lives on the Models tab
/// (<see cref="ProvidersViewModel"/>); secrets are written through <see cref="ISecretStore"/> and
/// never surfaced back in plaintext.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly ISecretStore _secrets;
    private readonly ThemeSyncService _theme;
    private readonly IModelCatalog _catalog;
    private readonly ActiveModelService _activeModel;
    private bool _initializing;

    public SettingsViewModel(
        ISettingsStore settings, ISecretStore secrets, ThemeSyncService theme,
        IModelCatalog catalog, ActiveModelService activeModel)
    {
        _settings = settings;
        _secrets = secrets;
        _theme = theme;
        _catalog = catalog;
        _activeModel = activeModel;

        var f = _settings.Current.Features;
        _initializing = true;
        ThemeIndex = (int)_settings.Current.Appearance.Theme;
        VisualIntelligenceEnabled = f.VisualIntelligence;
        WritingToolsEnabled = f.WritingTools;
        WebSearchEnabled = f.WebSearch;
        ImageGenerationEnabled = f.ImageGeneration;
        _initializing = false;
        AccentColor = _settings.Current.Appearance.AccentColor;

        RefreshWebSearch();
    }

    // ── Appearance ────────────────────────────────────────────────────────────

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    public string? AccentColor { get; private set; }

    partial void OnThemeIndexChanged(int value)
    {
        if (_initializing) return;
        _settings.Current.Appearance.Theme = (ThemeMode)value;
        _ = _settings.SaveAsync();
        _theme.Refresh();
    }

    public async Task SetAccentAsync(string? hex)
    {
        AccentColor = hex;
        _settings.Current.Appearance.AccentColor = hex;
        await _settings.SaveAsync();
        _theme.Refresh();
    }

    // ── Per-capability model pickers ────────────────────────────────────────────

    public ObservableCollection<ModelOption> ChatModels { get; } = new();
    public ObservableCollection<ModelOption> VisionModels { get; } = new();
    public ObservableCollection<ModelOption> WritingModels { get; } = new();
    public ObservableCollection<ModelOption> ImageModels { get; } = new();

    [ObservableProperty] public partial ModelOption? ChatModel { get; set; }
    [ObservableProperty] public partial ModelOption? VisionModel { get; set; }
    [ObservableProperty] public partial ModelOption? WritingModel { get; set; }
    [ObservableProperty] public partial ModelOption? ImageModel { get; set; }

    /// <summary>Loads the model catalog and selects each capability's current model. Call on page load.</summary>
    public async Task InitializeAsync()
    {
        var models = await _catalog.GetModelsAsync();
        var providers = _settings.Current.Providers.ToDictionary(p => p.Id);

        Fill(ChatModels, models);
        Fill(VisionModels, models.Where(m => Supports(providers, m, vision: true)));
        Fill(WritingModels, models);
        Fill(ImageModels, models.Where(m => Supports(providers, m, image: true)));

        _initializing = true;
        ChatModel = MatchOrSynthesize(ChatModels, _settings.Current.Capabilities.Chat, providers);
        VisionModel = Match(VisionModels, _settings.Current.Capabilities.VisualIntelligence);
        WritingModel = Match(WritingModels, _settings.Current.Capabilities.WritingTools);
        ImageModel = Match(ImageModels, _settings.Current.Capabilities.ImageGeneration);
        _initializing = false;
    }

    partial void OnChatModelChanged(ModelOption? value)
    {
        if (_initializing || value is null) return;
        _ = _activeModel.SetAsync(value.ProviderId, value.ModelId);
    }

    partial void OnVisionModelChanged(ModelOption? value) => Persist(s => s.VisualIntelligence = ToSelection(value));
    partial void OnWritingModelChanged(ModelOption? value) => Persist(s => s.WritingTools = ToSelection(value));
    partial void OnImageModelChanged(ModelOption? value) => Persist(s => s.ImageGeneration = ToSelection(value));

    private void Persist(Action<CapabilityRouting> apply)
    {
        if (_initializing) return;
        apply(_settings.Current.Capabilities);
        _ = _settings.SaveAsync();
    }

    private static ModelSelection? ToSelection(ModelOption? m) =>
        m is null ? null : new ModelSelection(m.ProviderId, m.ModelId);

    // ── Feature toggles ──────────────────────────────────────────────────────

    [ObservableProperty] public partial bool VisualIntelligenceEnabled { get; set; }
    [ObservableProperty] public partial bool WritingToolsEnabled { get; set; }
    [ObservableProperty] public partial bool WebSearchEnabled { get; set; }
    [ObservableProperty] public partial bool ImageGenerationEnabled { get; set; }

    partial void OnVisualIntelligenceEnabledChanged(bool value) => SetFeature(f => f.VisualIntelligence = value);
    partial void OnWritingToolsEnabledChanged(bool value) => SetFeature(f => f.WritingTools = value);
    partial void OnWebSearchEnabledChanged(bool value) => SetFeature(f => f.WebSearch = value);
    partial void OnImageGenerationEnabledChanged(bool value) => SetFeature(f => f.ImageGeneration = value);

    private void SetFeature(Action<FeatureSettings> apply)
    {
        if (_initializing) return;
        apply(_settings.Current.Features);
        _ = _settings.SaveAsync();
    }

    // ── Web search key (Tavily) ─────────────────────────────────────────────────

    [ObservableProperty] public partial string WebSearchStatus { get; set; } = string.Empty;
    [ObservableProperty] public partial bool WebSearchConfigured { get; set; }

    public void SaveWebSearchKey(string key)
    {
        if (!string.IsNullOrWhiteSpace(key))
            _secrets.Set(SecretKeys.TavilyApiKey, key.Trim());
        RefreshWebSearch();
    }

    public void ClearWebSearchKey()
    {
        _secrets.Delete(SecretKeys.TavilyApiKey);
        RefreshWebSearch();
    }

    private void RefreshWebSearch()
    {
        WebSearchConfigured = !string.IsNullOrWhiteSpace(_secrets.Get(SecretKeys.TavilyApiKey));
        WebSearchStatus = WebSearchConfigured ? "Key set ✓" : "No key set";
    }

    // ── Hotkeys ────────────────────────────────────────────────────────────────

    public HotkeyChord SummonHotkey => _settings.Current.Features.SummonHotkey;
    public HotkeyChord WritingHotkey => _settings.Current.Features.WritingToolsHotkey;
    public HotkeyChord VisionHotkey => _settings.Current.Features.VisualIntelligenceHotkey;

    public string SummonHotkeyText => Format(_settings.Current.Features.SummonHotkey);
    public string WritingHotkeyText => Format(_settings.Current.Features.WritingToolsHotkey);
    public string VisionHotkeyText => Format(_settings.Current.Features.VisualIntelligenceHotkey);

    public async Task SetSummonHotkeyAsync(HotkeyChord chord)
    {
        _settings.Current.Features.SummonHotkey = chord;
        await _settings.SaveAsync();
        OnPropertyChanged(nameof(SummonHotkeyText));
    }

    public async Task SetWritingHotkeyAsync(HotkeyChord chord)
    {
        _settings.Current.Features.WritingToolsHotkey = chord;
        await _settings.SaveAsync();
        OnPropertyChanged(nameof(WritingHotkeyText));
    }

    public async Task SetVisionHotkeyAsync(HotkeyChord chord)
    {
        _settings.Current.Features.VisualIntelligenceHotkey = chord;
        await _settings.SaveAsync();
        OnPropertyChanged(nameof(VisionHotkeyText));
    }

    /// <summary>Human-readable chord, e.g. "Alt + Space".</summary>
    public static string Format(HotkeyChord chord)
    {
        var parts = new List<string>();
        if ((chord.Modifiers & HotkeyChord.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((chord.Modifiers & HotkeyChord.MOD_ALT) != 0) parts.Add("Alt");
        if ((chord.Modifiers & HotkeyChord.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((chord.Modifiers & HotkeyChord.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName(chord.VirtualKey));
        return string.Join(" + ", parts);
    }

    private static string KeyName(uint vk) => vk switch
    {
        0x20 => "Space",
        >= 0x41 and <= 0x5A => ((char)vk).ToString(),
        >= 0x30 and <= 0x39 => ((char)vk).ToString(),
        >= 0x70 and <= 0x7B => $"F{vk - 0x6F}",
        _ => $"Key {vk:X}",
    };

    // ── helpers ─────────────────────────────────────────────────────────────────

    private static void Fill(ObservableCollection<ModelOption> target, IEnumerable<ModelOption> source)
    {
        target.Clear();
        foreach (var m in source)
            target.Add(m);
    }

    private static ModelOption? Match(ObservableCollection<ModelOption> options, ModelSelection? selection)
    {
        if (selection is null || selection.IsEmpty)
            return null;
        return options.FirstOrDefault(m => m.ProviderId == selection.ProviderId && m.ModelId == selection.ModelId);
    }

    /// <summary>
    /// Like <see cref="Match"/>, but if the saved selection isn't in the catalog this run (e.g. a
    /// local server that hasn't started), it surfaces the saved model as a synthesized option so the
    /// picker shows the user's real choice instead of appearing blank — which reads as a reset.
    /// </summary>
    private static ModelOption MatchOrSynthesize(
        ObservableCollection<ModelOption> options, ModelSelection selection,
        IReadOnlyDictionary<string, ProviderConnection> providers)
    {
        if (Match(options, selection) is { } found)
            return found;

        providers.TryGetValue(selection.ProviderId, out var provider);
        var isLocal = provider is { Kind: ProviderKind.Ollama }
            || (provider is not null
                && Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var u)
                && (u.IsLoopback || u.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)));
        var suffix = provider is null ? string.Empty : $" ({provider.DisplayName})";
        var option = new ModelOption(selection.ProviderId, selection.ModelId, $"{selection.ModelId}{suffix}", isLocal);
        options.Insert(0, option);
        return option;
    }

    private static bool Supports(IReadOnlyDictionary<string, ProviderConnection> providers, ModelOption m, bool vision = false, bool image = false)
    {
        if (!providers.TryGetValue(m.ProviderId, out var c))
            return false;
        return (!vision || c.SupportsVision) && (!image || c.SupportsImageGeneration);
    }
}
