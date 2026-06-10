using CommunityToolkit.Mvvm.ComponentModel;

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

    [ObservableProperty]
    public partial string Text { get; set; }
}
