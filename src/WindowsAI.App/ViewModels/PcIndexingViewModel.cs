using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowsAI.Core.Configuration;
using WindowsAI.Services.Search;

namespace WindowsAI_App.ViewModels;

/// <summary>
/// Backs the PC Indexing surface (Settings card + onboarding step): the folders the user has added
/// to the Windows Search scope and the indexer's live status/progress. The app builds no index of
/// its own — it manages the OS scope via <see cref="WindowsSearchIndexService"/> and polls status.
/// </summary>
public partial class PcIndexingViewModel : ObservableObject
{
    private readonly ISettingsStore _settings;
    private readonly WindowsSearchIndexService _index;
    private bool _initializing;

    public PcIndexingViewModel(ISettingsStore settings, WindowsSearchIndexService index)
    {
        _settings = settings;
        _index = index;
        Reload();
    }

    public ObservableCollection<string> Folders { get; } = new();

    public bool HasFolders => Folders.Count > 0;

    /// <summary>Mirrors <see cref="FeatureSettings.PcIndexing"/> — gates the search_my_pc tools.</summary>
    [ObservableProperty]
    public partial bool Enabled { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Checking…";

    [ObservableProperty]
    public partial bool IsIndexing { get; set; }

    /// <summary>0–100 indexed fraction (indeterminate while remaining is unknown).</summary>
    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    public void Reload()
    {
        Folders.Clear();
        foreach (var folder in _settings.Current.LocalIndex.Folders)
            Folders.Add(folder);

        _initializing = true;
        Enabled = _settings.Current.Features.PcIndexing;
        _initializing = false;
        OnPropertyChanged(nameof(HasFolders));
    }

    partial void OnEnabledChanged(bool value)
    {
        if (_initializing)
            return;
        _settings.Current.Features.PcIndexing = value;
        _ = _settings.SaveAsync();
    }

    public async Task AddFolderAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Folders.Contains(path, StringComparer.OrdinalIgnoreCase))
            return;

        await Task.Run(() => _index.AddFolder(path));
        _settings.Current.LocalIndex.Folders.Add(path);
        await _settings.SaveAsync();
        Folders.Add(path);
        OnPropertyChanged(nameof(HasFolders));
        await RefreshStatusAsync();
    }

    public async Task RemoveFolderAsync(string path)
    {
        await Task.Run(() => _index.RemoveFolder(path));
        _settings.Current.LocalIndex.Folders.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        await _settings.SaveAsync();
        Folders.Remove(path);
        OnPropertyChanged(nameof(HasFolders));
        await RefreshStatusAsync();
    }

    /// <summary>Polls the indexer and updates the progress text/bar. Safe to call repeatedly.</summary>
    public async Task RefreshStatusAsync()
    {
        var status = await Task.Run(_index.GetStatus);

        if (!status.Available)
        {
            IsIndexing = false;
            ProgressPercent = 0;
            StatusText = status.Message ?? "Windows Search is unavailable.";
            return;
        }

        if (status.IsUpToDate)
        {
            IsIndexing = false;
            ProgressPercent = 100;
            StatusText = $"Up to date · {status.ItemsIndexed:N0} items indexed";
            return;
        }

        IsIndexing = true;
        var total = status.ItemsIndexed + status.ItemsRemaining;
        ProgressPercent = total > 0 ? status.ItemsIndexed * 100.0 / total : 0;
        StatusText = $"Indexing… {status.ItemsRemaining:N0} item(s) remaining";
    }

    public void OpenIndexingOptions() => _index.OpenIndexingOptions();

    public static IReadOnlyList<string> SuggestedFolders()
    {
        string[] candidates =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        ];
        return candidates.Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p)).ToList();
    }
}
