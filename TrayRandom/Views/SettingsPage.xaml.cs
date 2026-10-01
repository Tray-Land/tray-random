using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrayRandom.Categories;
using TrayRandom.Services;
using Windows.ApplicationModel;

namespace TrayRandom.Views;

/// <summary>Settings shown inside the flyout: few enough that a separate window isn't worth it.</summary>
public sealed partial class SettingsPage : Page
{
    private readonly Action _goBack;
    private bool _loading = true;

    public SettingsPage(Action goBack)
    {
        _goBack = goBack;
        InitializeComponent();

        foreach ((string code, string name) in Locales.All)
        {
            LocaleBox.Items.Add(new ComboBoxItem { Content = name, Tag = code });
        }

        VersionText.Text = $"{App.DisplayName} {GetVersion()} · Fake data by Bogus";
        _loading = false;
    }

    /// <summary>Re-reads everything: startup can change in Windows Settings while we're hidden.</summary>
    public void OnShown()
    {
        _loading = true;
        string locale = SettingsService.Locale;
        LocaleBox.SelectedItem = LocaleBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == locale);
        AutoCopyToggle.IsOn = SettingsService.AutoCopy;
        UpdateFavoritesDescription();
        _loading = false;

        _ = LoadStartupStateAsync();
        BackButton.Focus(FocusState.Programmatic);
    }

    private void UpdateFavoritesDescription()
    {
        int count = SettingsService.Favorites.Count;
        FavoritesDescription.Text = count == 1 ? "1 pinned category. Reset restores the defaults." : $"{count} pinned categories. Reset restores the defaults.";
    }

    private static string GetVersion()
    {
        try
        {
            PackageVersion v = Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch
        {
            return "(unpackaged)";
        }
    }

    private async Task LoadStartupStateAsync()
    {
        ShowStartupState(await StartupService.GetStateAsync());
    }

    private void ShowStartupState(StartupTaskState? state)
    {
        _loading = true;
        StartupToggle.IsOn = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

        // The user (Task Manager, Settings > Apps > Startup) or policy has the final say; the app
        // can't override it, so say where to change it instead of offering a dead toggle.
        StartupToggle.IsEnabled = state is StartupTaskState.Enabled or StartupTaskState.Disabled;
        StartupDescription.Text = state switch
        {
            StartupTaskState.DisabledByUser => "Turned off in Settings > Apps > Startup. Turn it on there.",
            StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy => "Managed by your organization.",
            null => "Only available when the app is installed.",
            _ => "Keep the tray icon ready after you sign in.",
        };
        _loading = false;
    }

    private async void StartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        ShowStartupState(await StartupService.SetEnabledAsync(StartupToggle.IsOn));
    }

    private void LocaleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && LocaleBox.SelectedItem is ComboBoxItem { Tag: string code })
        {
            SettingsService.Locale = code;
        }
    }

    private void AutoCopyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (!_loading)
        {
            SettingsService.AutoCopy = AutoCopyToggle.IsOn;
        }
    }

    private void ResetFavoritesButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsService.Favorites = CategoryCatalog.DefaultFavorites;
        UpdateFavoritesDescription();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => _goBack();
}
