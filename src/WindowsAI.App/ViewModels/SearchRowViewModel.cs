using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;
using WindowsAI.Core.Search;
using WindowsAI_App.Controls;
using WindowsAI_App.Helpers;

namespace WindowsAI_App.ViewModels;

/// <summary>
/// One row in the overlay's live-search list: a section header, a launchable hit, or the
/// pinned "Ask AI" row. Selection (for keyboard navigation) is the only mutable state.
/// </summary>
public partial class SearchRowViewModel : ObservableObject
{
    private SearchRowViewModel(string title, string subtitle, string glyph, SearchHit? hit, bool isHeader, bool isAskAi)
    {
        Title = title;
        Subtitle = subtitle;
        Glyph = glyph;
        Hit = hit;
        IsHeader = isHeader;
        IsAskAi = isAskAi;
    }

    public static SearchRowViewModel Header(string title) =>
        new(title, string.Empty, string.Empty, hit: null, isHeader: true, isAskAi: false);

    public static SearchRowViewModel ForHit(SearchHit hit)
    {
        var row = new SearchRowViewModel(hit.Name, hit.Detail, GlyphFor(hit), hit, isHeader: false, isAskAi: false);
        row.BeginLoadIcon(hit.Target);
        return row;
    }

    public static SearchRowViewModel AskAi(string query) =>
        new($"Ask AI about “{query}”", string.Empty, "", hit: null, isHeader: false, isAskAi: true);

    public string Title { get; }
    public string Subtitle { get; }
    public string Glyph { get; }
    public SearchHit? Hit { get; }
    public bool IsHeader { get; }
    public bool IsAskAi { get; }

    public bool IsSelectable => !IsHeader;
    public bool HasSubtitle => Subtitle.Length > 0;

    /// <summary>The real shell icon/thumbnail, loaded async; null until ready (glyph shows meanwhile).</summary>
    [ObservableProperty]
    public partial ImageSource? Icon { get; set; }

    /// <summary>Show the loaded shell icon once it arrives.</summary>
    public bool ShowImage => Icon is not null;

    /// <summary>Show the glyph placeholder for hits until their real icon loads (never for Ask-AI).</summary>
    public bool ShowGlyph => Icon is null && !IsAskAi;

    partial void OnIconChanged(ImageSource? value)
    {
        OnPropertyChanged(nameof(ShowImage));
        OnPropertyChanged(nameof(ShowGlyph));
    }

    // Request well above the ~22px display size so the shell hands back a high-res native asset
    // that downsamples crisply on hi-DPI displays.
    private async void BeginLoadIcon(string target)
        => Icon = await ShellIconProvider.GetIconAsync(target, 96);

    /// <summary>Keyboard-navigation highlight; focus itself stays in the search box.</summary>
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    private static string GlyphFor(SearchHit hit) => hit.Kind switch
    {
        SearchHitKind.App => "",
        SearchHitKind.Folder => "",
        SearchHitKind.Setting => "",
        _ => FileGlyphs.For(hit.Name),
    };
}
