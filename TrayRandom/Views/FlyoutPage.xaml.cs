using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TrayRandom.Categories;
using TrayRandom.Favorites;
using TrayRandom.Services;
using Windows.System;
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace TrayRandom.Views;

/// <summary>
/// The flyout's content: favorites across the top, the category picker, the inputs the category
/// needs, and the results. Nothing runs while the flyout is hidden; generating is instant and
/// local, so there is no poller.
/// </summary>
public sealed partial class FlyoutPage : Page, IDisposable
{
    private const string StarGlyph = "";
    private const string StarFilledGlyph = "";
    private const int LargeResultLength = 8;
    private const int MediumResultLength = 28;

    private static readonly TimeSpan StatusDuration = TimeSpan.FromSeconds(2.5);

    private readonly ObservableCollection<FavoriteChip> _favorites = [];
    private readonly DispatcherQueueTimer _statusTimer;
    private RandomContentGenerator _generator;
    private RandomCategory _category;
    private IReadOnlyList<GeneratedItem> _results = [];
    private bool _loading = true;
    private bool _resultsStale;

    public FlyoutPage()
    {
        InitializeComponent();
        TitleText.Text = App.DisplayName;

        _statusTimer = DispatcherQueue.CreateTimer();
        _statusTimer.Interval = StatusDuration;
        _statusTimer.IsRepeating = false;
        _statusTimer.Tick += (_, _) => StatusText.Text = string.Empty;

        _generator = new RandomContentGenerator(SettingsService.Locale);
        FavoritesView.ItemsSource = _favorites;
        BuildCategoryMenu();
        AddFavoriteShortcuts();

        CategoryInputs inputs = SettingsService.Inputs;
        DiceBox.Text = inputs.DiceNotation;
        MinBox.Value = inputs.RangeMin;
        MaxBox.Value = inputs.RangeMax;
        ListItemsBox.Text = inputs.ListText;
        CountBox.Value = SettingsService.Count;

        _category = CategoryCatalog.Find(SettingsService.LastCategoryId)!;
        LoadFavorites();
        SelectCategory(_category, generate: false);

        SettingsService.Changed += OnSettingsChanged;
        _loading = false;
    }

    /// <summary>The flyout opened: make something to show if there's nothing yet.</summary>
    public void OnShown()
    {
        // Settings may have reset the favorites while this page was swapped out.
        LoadFavorites();
        UpdateFavoriteButton();

        if (_results.Count == 0 || _resultsStale)
        {
            _resultsStale = false;
            Generate(copy: false);
        }

        GenerateButton.Focus(FocusState.Programmatic);
    }

    /// <summary>The flyout hid: keep what the user typed for next time.</summary>
    public void OnHidden() => SaveInputs();

    public void Dispose()
    {
        _statusTimer.Stop();

        // A static event would keep the closed window's whole XAML tree alive.
        SettingsService.Changed -= OnSettingsChanged;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_generator.Locale != SettingsService.Locale)
        {
            _generator = new RandomContentGenerator(SettingsService.Locale);
            _resultsStale = true;
        }
    }

    private void BuildCategoryMenu()
    {
        foreach (CategoryGroup group in CategoryCatalog.Groups)
        {
            MenuFlyoutSubItem sub = new() { Text = group.Name, Icon = new FontIcon { Glyph = group.Glyph } };
            foreach (RandomCategory category in CategoryCatalog.All.Where(c => c.Group == group))
            {
                MenuFlyoutItem item = new() { Text = category.Name };
                item.Click += (_, _) => SelectCategory(category, generate: true);
                sub.Items.Add(item);
            }

            CategoryMenu.Items.Add(sub);
        }
    }

    private void AddFavoriteShortcuts()
    {
        for (int i = 1; i <= 9; i++)
        {
            int index = i - 1;
            KeyboardAccelerator accelerator = new() { Key = VirtualKey.Number0 + i, Modifiers = VirtualKeyModifiers.Control };
            accelerator.Invoked += (_, args) =>
            {
                args.Handled = true;
                if (index < _favorites.Count)
                {
                    SelectCategory(_favorites[index].Category, generate: true);
                }
            };
            KeyboardAccelerators.Add(accelerator);
        }
    }

    private void SelectCategory(RandomCategory category, bool generate)
    {
        bool changed = category != _category;
        SaveInputs();
        _category = category;
        SettingsService.LastCategoryId = category.Id;

        CategoryGlyph.Glyph = category.Glyph;
        CategoryNameText.Text = category.Name;
        CategoryGroupText.Text = category.Group.Name;
        AutomationProperties.SetName(CategoryButton, $"Category: {category.Name}");
        GenerateButton.Content = category.ActionVerb;

        DicePanel.Visibility = category.Input == CategoryInput.Dice ? Visibility.Visible : Visibility.Collapsed;
        RangePanel.Visibility = category.Input == CategoryInput.Range ? Visibility.Visible : Visibility.Collapsed;
        ListItemsBox.Visibility = category.Input == CategoryInput.List ? Visibility.Visible : Visibility.Collapsed;

        UpdateFavoriteButton();
        FavoritesView.SelectedItem = _favorites.FirstOrDefault(f => f.Id == category.Id);
        if (FavoritesView.SelectedItem is { } selected)
        {
            FavoritesView.ScrollIntoView(selected);
        }

        if (changed)
        {
            StatusBar.IsOpen = false;
            _results = [];
            ShowResults();
        }

        if (generate)
        {
            Generate(copy: SettingsService.AutoCopy);
        }
    }

    private void Generate(bool copy)
    {
        SaveInputs();
        try
        {
            _results = _generator.Generate(_category, SettingsService.Inputs, SettingsService.Count);
            StatusBar.IsOpen = false;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            StatusBar.Title = "Can't generate that";
            StatusBar.Message = ex.Message;
            StatusBar.IsOpen = true;
            return;
        }

        ShowResults();
        if (copy)
        {
            CopyResults();
        }
    }

    private void ShowResults()
    {
        bool single = _results.Count <= 1;
        SingleResultPanel.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.Visibility = single ? Visibility.Collapsed : Visibility.Visible;

        if (!single)
        {
            ResultsList.ItemsSource = _results.Select((item, i) => new ResultRow(item, i + 1)).ToArray();
            return;
        }

        ResultsList.ItemsSource = null;
        GeneratedItem? result = _results.Count == 1 ? _results[0] : null;
        string value = result?.Value ?? string.Empty;
        ResultText.Text = value;
        DetailText.Text = result?.Detail ?? string.Empty;
        DetailText.Visibility = string.IsNullOrEmpty(DetailText.Text) ? Visibility.Collapsed : Visibility.Visible;

        // Big for a dice total, smaller for a name, body text for paragraphs and profiles.
        bool multiline = value.Contains('\n');
        string style = multiline || value.Length > MediumResultLength ? "BodyLargeTextBlockStyle"
            : value.Length > LargeResultLength ? "SubtitleTextBlockStyle"
            : "DisplayTextBlockStyle";
        ResultText.Style = (Style)Application.Current.Resources[style];
        ResultText.TextAlignment = multiline || value.Length > MediumResultLength ? TextAlignment.Left : TextAlignment.Center;
        ResultText.HorizontalAlignment = ResultText.TextAlignment == TextAlignment.Left ? HorizontalAlignment.Stretch : HorizontalAlignment.Center;
    }

    private void CopyResults()
    {
        if (_results.Count == 0)
        {
            return;
        }

        CopyText(string.Join(Environment.NewLine, _results.Select(r => r.Value)), _results.Count == 1 ? "Copied" : $"Copied {_results.Count}");
    }

    private void CopyText(string text, string confirmation)
    {
        ShowStatus(ClipboardService.TryCopy(text) ? confirmation : "Couldn't copy; try again");
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void SaveInputs()
    {
        if (_loading)
        {
            return;
        }

        CategoryInputs current = SettingsService.Inputs;
        CategoryInputs updated = current with
        {
            DiceNotation = DiceBox.Text.Trim(),
            RangeMin = double.IsNaN(MinBox.Value) ? current.RangeMin : (long)Math.Round(MinBox.Value),
            RangeMax = double.IsNaN(MaxBox.Value) ? current.RangeMax : (long)Math.Round(MaxBox.Value),
            ListText = ListItemsBox.Text,
        };
        if (updated != current)
        {
            SettingsService.Inputs = updated;
        }
    }

    // Favorites

    private void LoadFavorites()
    {
        _favorites.Clear();
        int position = 1;
        foreach (string id in SettingsService.Favorites)
        {
            _favorites.Add(new FavoriteChip(CategoryCatalog.Find(id)!, position++));
        }

        bool any = _favorites.Count > 0;
        FavoritesView.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        NoFavoritesText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        FavoritesView.SelectedItem = _favorites.FirstOrDefault(f => f.Id == _category.Id);
    }

    private void SaveFavorites(IReadOnlyList<string> ids)
    {
        SettingsService.Favorites = ids;
        LoadFavorites();
        UpdateFavoriteButton();
    }

    private void UpdateFavoriteButton()
    {
        bool isFavorite = SettingsService.Favorites.Contains(_category.Id);
        FavoriteGlyph.Glyph = isFavorite ? StarFilledGlyph : StarGlyph;
        string label = isFavorite ? "Remove from favorites" : "Add to favorites";
        AutomationProperties.SetName(FavoriteButton, label);
        ToolTipService.SetToolTip(FavoriteButton, $"{label} (Ctrl+D)");
    }

    private void ToggleFavorite(string id)
    {
        IReadOnlyList<string> current = SettingsService.Favorites;
        IReadOnlyList<string> updated = FavoritesList.Toggle(current, id);
        if (updated.Count == current.Count)
        {
            ShowStatus($"Up to {FavoritesList.MaxFavorites} favorites");
            return;
        }

        SaveFavorites(updated);
        ShowStatus(updated.Count > current.Count ? "Added to favorites" : "Removed from favorites");
    }

    private void FavoritesView_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FavoriteChip chip)
        {
            SelectCategory(chip.Category, generate: true);
        }
    }

    private void FavoritesView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        SaveFavorites(_favorites.Select(f => f.Id).ToArray());

    // Right-click or Shift+F10 on a chip: reorder without dragging, or unpin.
    private void FavoriteChip_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { DataContext: FavoriteChip chip })
        {
            return;
        }

        args.Handled = true;
        int index = _favorites.IndexOf(chip);
        MenuFlyout menu = new();
        MenuFlyoutItem left = new() { Text = "Move left", Icon = new FontIcon { Glyph = "" }, IsEnabled = index > 0 };
        left.Click += (_, _) => SaveFavorites(FavoritesList.Move(SettingsService.Favorites, chip.Id, -1));
        MenuFlyoutItem right = new() { Text = "Move right", Icon = new FontIcon { Glyph = "" }, IsEnabled = index < _favorites.Count - 1 };
        right.Click += (_, _) => SaveFavorites(FavoritesList.Move(SettingsService.Favorites, chip.Id, 1));
        MenuFlyoutItem remove = new() { Text = "Remove from favorites", Icon = new FontIcon { Glyph = "" } };
        remove.Click += (_, _) => ToggleFavorite(chip.Id);
        menu.Items.Add(left);
        menu.Items.Add(right);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(remove);

        if (args.TryGetPosition(sender, out Windows.Foundation.Point point))
        {
            menu.ShowAt(sender, point);
        }
        else
        {
            menu.ShowAt((FrameworkElement)sender);
        }
    }

    private void FavoriteButton_Click(object sender, RoutedEventArgs e) => ToggleFavorite(_category.Id);

    private void FavoriteAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleFavorite(_category.Id);
    }

    private void NextFavorite_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => CycleFavorite(args, 1);

    private void PreviousFavorite_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) => CycleFavorite(args, -1);

    private void CycleFavorite(KeyboardAcceleratorInvokedEventArgs args, int direction)
    {
        args.Handled = true;
        if (FavoritesList.Cycle(SettingsService.Favorites, _category.Id, direction) is { } id)
        {
            SelectCategory(CategoryCatalog.Find(id)!, generate: true);
        }
    }

    // Inputs and actions

    [GeneratedRegex(@"^(\d*)d(\d+|%)$", RegexOptions.IgnoreCase)]
    private static partial Regex SimpleDice();

    private void DicePreset_Click(object sender, RoutedEventArgs e)
    {
        // Keep the number of dice when switching size: 3d6 → d20 gives 3d20.
        string sides = (string)((FrameworkElement)sender).Tag;
        Match match = SimpleDice().Match(DiceBox.Text.Trim());
        string count = match.Success ? match.Groups[1].Value : string.Empty;
        DiceBox.Text = $"{count}d{sides}";
        Generate(copy: SettingsService.AutoCopy);
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            Generate(copy: SettingsService.AutoCopy);
        }
    }

    private void CountBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (double.IsNaN(args.NewValue))
        {
            sender.Value = 1;
            return;
        }

        SettingsService.Count = (int)args.NewValue;
        if (!_loading && _results.Count > 0 && _results.Count != SettingsService.Count)
        {
            Generate(copy: false);
        }
    }

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ResultRow row)
        {
            CopyText(row.Value, $"Copied #{row.Number}");
        }
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e) => Generate(copy: SettingsService.AutoCopy);

    private void GenerateAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Generate(copy: SettingsService.AutoCopy);
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e) => CopyResults();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();
}
