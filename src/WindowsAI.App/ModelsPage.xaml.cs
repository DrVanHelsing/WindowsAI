using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WindowsAI.Core.Providers;
using WindowsAI.Core.Security;
using WindowsAI_App.Controls;
using WindowsAI_App.ViewModels;

namespace WindowsAI_App;

/// <summary>
/// Models &amp; providers hub: manage the user's provider connections (add / edit / test / remove /
/// enable / sign in) and pick the default chat model from everything they've configured. The model
/// list is bound to the shared <see cref="ChatViewModel"/> so the choice reflects everywhere.
/// </summary>
public sealed partial class ModelsPage : Page
{
    public ChatViewModel ViewModel { get; }
    public ProvidersViewModel Providers { get; }

    /// <summary>Models bucketed by Kind ("Local" / "Cloud") for the grouped list.</summary>
    public ObservableCollection<ModelGroup> Groups { get; } = new();

    public ModelsPage()
    {
        ViewModel = App.GetService<ChatViewModel>();
        Providers = App.GetService<ProvidersViewModel>();
        InitializeComponent();

        ViewModel.AvailableModels.CollectionChanged += OnModelsChanged;
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Providers.Reload();
        await ViewModel.InitializeAsync();
    }

    private void OnModelsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildGroups();

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

    /// <summary>Segoe Fluent glyph for a model row: a PC for on-device, a cloud otherwise.</summary>
    public static string GlyphFor(bool isLocal) => isLocal ? "" : "";

    // ── Provider card actions ────────────────────────────────────────────────

    private async void AddProvider_Click(object sender, RoutedEventArgs e)
    {
        var result = await ProviderEditDialog.ShowAsync(XamlRoot, Providers.Probe, App.GetService<ISecretStore>(), existing: null);
        if (result is null)
            return;

        var saved = await Providers.AddAsync(result.Connection, result.ApiKey);
        if (result.ClientSecret is not null)
            await Providers.SetClientSecretAsync(saved, result.ClientSecret);

        await ViewModel.InitializeAsync();
    }

    private async void ProviderEdit_Click(object sender, RoutedEventArgs e)
    {
        if (Card(sender) is not { } vm)
            return;

        var result = await ProviderEditDialog.ShowAsync(XamlRoot, Providers.Probe, App.GetService<ISecretStore>(), vm.Model);
        if (result is null)
            return;

        await Providers.UpdateAsync(result.Connection, result.ApiKey);
        if (result.ClientSecret is not null)
            await Providers.SetClientSecretAsync(result.Connection, result.ClientSecret);

        await ViewModel.InitializeAsync();
    }

    private async void ProviderTest_Click(object sender, RoutedEventArgs e)
    {
        if (Card(sender) is not { } vm)
            return;

        var result = await Providers.Probe.TestAsync(vm.Model);
        Providers.UpdateStatus(vm);
        await DialogService.ConfirmAsync(XamlRoot, $"Test · {vm.DisplayName}", result.Message, confirmText: "OK");
    }

    private async void ProviderRemove_Click(object sender, RoutedEventArgs e)
    {
        if (Card(sender) is not { } vm)
            return;

        if (await DialogService.ConfirmAsync(
                XamlRoot, $"Remove {vm.DisplayName}?",
                "This removes the connection and its stored key from this PC.",
                confirmText: "Remove", isDestructive: true))
        {
            await Providers.RemoveAsync(vm.Model);
            await ViewModel.InitializeAsync();
        }
    }

    private async void ProviderSignIn_Click(object sender, RoutedEventArgs e)
    {
        if (Card(sender) is not { } vm)
            return;

        try
        {
            await Providers.SignInAsync(vm.Model);
            Providers.UpdateStatus(vm);
        }
        catch (Exception ex)
        {
            await DialogService.ConfirmAsync(XamlRoot, "Google sign-in failed", ex.Message, confirmText: "OK");
        }
    }

    private async void ProviderEnabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || toggle.DataContext is not ProviderConnectionViewModel vm)
            return;
        if (toggle.IsOn == vm.Model.Enabled)
            return;

        await Providers.SetEnabledAsync(vm.Model, toggle.IsOn);
        Providers.UpdateStatus(vm);
        await ViewModel.InitializeAsync();
    }

    private static ProviderConnectionViewModel? Card(object sender)
        => (sender as FrameworkElement)?.DataContext as ProviderConnectionViewModel;
}

/// <summary>A kind bucket of models ("On this PC" / "Cloud").</summary>
public sealed class ModelGroup : List<ModelOption>
{
    public ModelGroup(string key) => Key = key;

    public string Key { get; }
}
