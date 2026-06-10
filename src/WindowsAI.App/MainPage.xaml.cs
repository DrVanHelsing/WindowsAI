using System.Collections.Specialized;
using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Windows.System;
using WindowsAI_App.ViewModels;

namespace WindowsAI_App;

public sealed partial class MainPage : Page
{
    public ChatViewModel ViewModel { get; }

    public MainPage()
    {
        ViewModel = App.GetService<ChatViewModel>();
        InitializeComponent();

        ViewModel.Messages.CollectionChanged += OnMessagesChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Loaded += OnLoaded;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        // "new" parameter means the New Chat button was tapped in the sidebar.
        if (e.Parameter is "new")
            ViewModel.NewConversationCommand.Execute(null);
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
        => await ViewModel.InitializeAsync();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ChatViewModel.IsBusy)) return;

        if (ViewModel.IsBusy)
            ComposerPulseStoryboard.Begin();
        else
        {
            ComposerPulseStoryboard.Stop();
            ComposerAmbientBar.Opacity = 0.30;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        EmptyState.Visibility = ViewModel.Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (ViewModel.Messages.Count > 0)
            ChatList.ScrollIntoView(ViewModel.Messages[^1]);
    }

    private void LocalContext_Toggled(object sender, RoutedEventArgs e)
        => ViewModel.UseLocalContext = LocalContextToggle.IsChecked == true;

    /// <summary>Suggestion chip: drop its prompt into the composer ready to edit/send.</summary>
    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Input = (string)((FrameworkElement)sender).Tag;
        InputBox.Focus(FocusState.Programmatic);
        InputBox.SelectionStart = InputBox.Text.Length;
    }

    /// <summary>Opens a RAG source file in its default app. Failure is silent — the file may be gone.</summary>
    private void Source_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WindowsAI.Core.Context.LocalDocument doc)
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(doc.Path)
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            // Best effort — opening a moved/locked file shouldn't disrupt the chat.
        }
    }

    private void CopyMessage_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is not ChatMessageViewModel message)
            return;

        var package = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(message.Text);
        global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
    }

    // Hover-reveal for the per-message actions row inside the DataTemplate.
    private static void SetActionsRowOpacity(object templateRoot, double opacity)
    {
        if (templateRoot is FrameworkElement root &&
            root.FindName("ActionsRow") is FrameworkElement actions)
            actions.Opacity = opacity;
    }

    private void Message_PointerEntered(object sender, PointerRoutedEventArgs e) => SetActionsRowOpacity(sender, 1);

    private void Message_PointerExited(object sender, PointerRoutedEventArgs e) => SetActionsRowOpacity(sender, 0);

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter) return;

        var shift = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(global::Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (shift) return;

        e.Handled = true;
        if (ViewModel.SendCommand.CanExecute(null))
            ViewModel.SendCommand.Execute(null);
    }
}
