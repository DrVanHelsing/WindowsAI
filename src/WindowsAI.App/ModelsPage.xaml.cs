using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAI.Core.Providers;
using WindowsAI_App.ViewModels;

namespace WindowsAI_App;

/// <summary>
/// Model management: lists every available model (local and cloud, grouped) and lets the
/// user pick the active one. Bound to the shared <see cref="ChatViewModel"/> so the choice
/// is reflected immediately in the chat surface and the overlay.
/// </summary>
public sealed partial class ModelsPage : Page
{
    public ChatViewModel ViewModel { get; }

    /// <summary>Models bucketed by Kind ("Local" / "Cloud") for the grouped list.</summary>
    public ObservableCollection<ModelGroup> Groups { get; } = new();

    public ModelsPage()
    {
        ViewModel = App.GetService<ChatViewModel>();
        InitializeComponent();

        ViewModel.AvailableModels.CollectionChanged += OnModelsChanged;
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await ViewModel.InitializeAsync();

    private void OnModelsChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RebuildGroups();

    private void RebuildGroups()
    {
        Groups.Clear();
        foreach (var bucket in ViewModel.AvailableModels.GroupBy(m => m.Kind).OrderBy(g => g.Key == "Local" ? 0 : 1))
        {
            var group = new ModelGroup(bucket.Key == "Local" ? "On this PC" : "Cloud");
            group.AddRange(bucket);
            Groups.Add(group);
        }
    }

    /// <summary>Segoe Fluent glyph for a provider id — used by the item template.</summary>
    public static string GlyphFor(string providerId) => providerId switch
    {
        "ollama" => "", // PC
        "openrouter" => "", // network/cloud routing
        "gemini" => "", // cloud
        _ => "",
    };
}

/// <summary>A kind bucket of models ("On this PC" / "Cloud").</summary>
public sealed class ModelGroup : List<ModelOption>
{
    public ModelGroup(string key) => Key = key;

    public string Key { get; }
}
