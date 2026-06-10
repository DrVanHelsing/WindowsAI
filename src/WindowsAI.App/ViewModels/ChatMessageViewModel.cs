using CommunityToolkit.Mvvm.ComponentModel;
using WindowsAI.Core.Context;

namespace WindowsAI_App.ViewModels;

/// <summary>A single chat bubble. <see cref="Text"/> is observable so it can grow live while streaming.</summary>
public partial class ChatMessageViewModel : ObservableObject
{
    public ChatMessageViewModel(string author, string text, bool isUser)
    {
        Author = author;
        IsUser = isUser;
        Text = text;
    }

    public string Author { get; }

    public bool IsUser { get; }

    /// <summary>Creation time, shown in the hover actions row.</summary>
    public string TimeLabel { get; } = DateTime.Now.ToString("HH:mm");

    [ObservableProperty]
    public partial string Text { get; set; }

    /// <summary>Local files used as context for this reply (RAG source chips). Set once.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<LocalDocument>? Sources { get; set; }

    public bool HasSources => Sources is { Count: > 0 };

    partial void OnSourcesChanged(IReadOnlyList<LocalDocument>? value)
        => OnPropertyChanged(nameof(HasSources));
}
