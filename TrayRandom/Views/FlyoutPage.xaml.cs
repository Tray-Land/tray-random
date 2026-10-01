using System.Collections.ObjectModel;
using System.Numerics;
using System.Text.RegularExpressions;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using TrayRandom.Categories;
using TrayRandom.Dice;
using TrayRandom.Favorites;
using TrayRandom.Services;
using TrayRandom.Text;
using Windows.System;
using Windows.UI.ViewManagement;
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
    private static readonly TimeSpan ScrambleDuration = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan ScrambleFrame = TimeSpan.FromMilliseconds(40);

    private readonly ObservableCollection<FavoriteChip> _favorites = [];
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly DispatcherQueueTimer _scrambleTimer;
    private readonly Random _scrambleRandom = new();
    private string _scrambleResult = string.Empty;
    private string _scrambleDetail = string.Empty;
    private ResultRow[] _scrambleRows = [];
    private DateTime _scrambleStart;
    private readonly UISettings _uiSettings = new();
    private readonly AccessibilitySettings _accessibility = new();
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

        _scrambleTimer = DispatcherQueue.CreateTimer();
        _scrambleTimer.Interval = ScrambleFrame;
        _scrambleTimer.IsRepeating = true;
        _scrambleTimer.Tick += (_, _) => ScrambleTick();

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
        DiceVisualToggle.IsChecked = SettingsService.DiceVisual;
        PinButton.IsChecked = SettingsService.Pinned;
        UpdatePinGlyph();

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
    public void OnHidden()
    {
        SaveInputs();
        StopScramble();
        DiceTray.Stop();
    }

    public void Dispose()
    {
        _statusTimer.Stop();
        StopScramble();
        DiceTray.Stop();

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
        DiceVisualToggle.Visibility = DicePanel.Visibility;

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

        ShowResults(animate: true);
        if (copy)
        {
            CopyResults();
        }
    }

    private void ShowResults(bool animate = false)
    {
        bool single = _results.Count <= 1;
        SingleResultPanel.Visibility = single ? Visibility.Visible : Visibility.Collapsed;
        ResultsList.Visibility = single ? Visibility.Collapsed : Visibility.Visible;

        if (!single)
        {
            ShowDiceTray(null, animate: false);
            ResultRow[] rows = _results.Select((item, i) => new ResultRow(item, i + 1)).ToArray();
            ResultsList.ItemsSource = rows;
            StartScramble(string.Empty, string.Empty, animate ? rows : []);
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
        ShowDiceTray(result, animate);
        if (animate && DiceTray.Visibility != Visibility.Visible)
        {
            StartScramble(value, DetailText.Text, []);
        }
        else
        {
            StopScramble();
        }
    }

    // 400 ms of random letters that resolve left to right; the real text lands at the end.
    private void StartScramble(string result, string detail, ResultRow[] rows)
    {
        StopScramble();
        if (!SettingsService.TextAnimation || !_uiSettings.AnimationsEnabled || (result.Length == 0 && detail.Length == 0 && rows.Length == 0))
        {
            return;
        }

        _scrambleResult = result;
        _scrambleDetail = detail;
        _scrambleRows = rows;
        _scrambleStart = DateTime.UtcNow;
        ScrambleTick();
        _scrambleTimer.Start();
    }

    private void ScrambleTick()
    {
        double progress = (DateTime.UtcNow - _scrambleStart) / ScrambleDuration;
        if (progress >= 1)
        {
            StopScramble();
            return;
        }

        ResultText.Text = TextScramble.Frame(_scrambleResult, progress, _scrambleRandom);
        DetailText.Text = TextScramble.Frame(_scrambleDetail, progress, _scrambleRandom);
        foreach (ResultRow row in _scrambleRows)
        {
            row.Scramble(progress, _scrambleRandom);
        }
    }

    private void StopScramble()
    {
        if (!_scrambleTimer.IsRunning)
        {
            return;
        }

        _scrambleTimer.Stop();
        ResultText.Text = _scrambleResult;
        DetailText.Text = _scrambleDetail;
        foreach (ResultRow row in _scrambleRows)
        {
            row.Settle();
        }

        _scrambleRows = [];
    }

    // Animated dice: the tray takes the card's top and the total sits under it, hidden until
    // the dice land. Off, in High Contrast, or for anything but one dice roll, the card is all text.
    private void ShowDiceTray(GeneratedItem? result, bool animate)
    {
        IReadOnlyList<TrayDie> dice = SettingsService.DiceVisual && result?.Roll is { } roll && !_accessibility.HighContrast
            ? DiceTrayLayout.Flatten(roll)
            : [];
        bool visual = dice.Count > 0;
        DiceTray.Visibility = visual ? Visibility.Visible : Visibility.Collapsed;
        TrayRow.Height = visual ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ResultRow.Height = visual ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        SingleResultPanel.Padding = visual ? new Thickness(16, 0, 16, 12) : new Thickness(16);

        // A big roll's working would squeeze the tray; the dice show it anyway.
        DetailText.MaxLines = visual ? 2 : 0;

        if (!visual)
        {
            DiceTray.Clear();
            RevealResult(animate: false);
            return;
        }

        ResultText.Style = (Style)Application.Current.Resources["TitleLargeTextBlockStyle"];
        DiceTray.Show(
            dice.Take(DiceTrayLayout.MaxDice).ToArray(),
            Math.Max(0, dice.Count - DiceTrayLayout.MaxDice),
            animate && _uiSettings.AnimationsEnabled);

        if (DiceTray.IsRolling)
        {
            HideResult();
        }
        else
        {
            RevealResult(animate: false);
        }
    }

    private void HideResult()
    {
        foreach (UIElement element in (UIElement[])[ResultText, DetailText])
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.StopAnimation(nameof(Visual.Scale));
            visual.Opacity = 0;
        }
    }

    // The total springs up from small as the dice settle; the working fades in after it.
    private void RevealResult(bool animate)
    {
        Visual total = ElementCompositionPreview.GetElementVisual(ResultText);
        Visual detail = ElementCompositionPreview.GetElementVisual(DetailText);
        foreach (Visual visual in (Visual[])[total, detail])
        {
            visual.StopAnimation(nameof(Visual.Opacity));
            visual.StopAnimation(nameof(Visual.Scale));
            visual.Opacity = 1;
            visual.Scale = Vector3.One;
        }

        if (!animate || !_uiSettings.AnimationsEnabled)
        {
            return;
        }

        Compositor compositor = total.Compositor;
        total.CenterPoint = new Vector3((float)ResultText.ActualWidth / 2, (float)ResultText.ActualHeight / 2, 0);
        SpringVector3NaturalMotionAnimation spring = compositor.CreateSpringVector3Animation();
        spring.InitialValue = new Vector3(0.4f, 0.4f, 1);
        spring.FinalValue = Vector3.One;
        spring.DampingRatio = 0.5f;
        spring.Period = TimeSpan.FromMilliseconds(50);
        total.StartAnimation(nameof(Visual.Scale), spring);

        ScalarKeyFrameAnimation fadeIn = compositor.CreateScalarKeyFrameAnimation();
        fadeIn.InsertKeyFrame(0, 0);
        fadeIn.InsertKeyFrame(1, 1);
        fadeIn.Duration = TimeSpan.FromMilliseconds(160);
        total.StartAnimation(nameof(Visual.Opacity), fadeIn);

        fadeIn.Duration = TimeSpan.FromMilliseconds(250);
        fadeIn.DelayTime = TimeSpan.FromMilliseconds(120);
        fadeIn.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        detail.StartAnimation(nameof(Visual.Opacity), fadeIn);
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

        // Several results are a list, which the dice tray can't show: asking for more turns it off.
        if (SettingsService.Count > 1 && SettingsService.DiceVisual)
        {
            SettingsService.DiceVisual = false;
            DiceVisualToggle.IsChecked = false;
        }

        if (!_loading && _results.Count > 0 && _results.Count != SettingsService.Count)
        {
            Generate(copy: false);
        }
    }

    private void ResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ResultRow row)
        {
            CopyText(row.FinalValue, $"Copied #{row.Number}");
        }
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e) => Generate(copy: SettingsService.AutoCopy);

    private void GenerateAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Generate(copy: SettingsService.AutoCopy);
    }

    // Checked/Unchecked rather than Click: UI Automation's Toggle (Narrator) doesn't raise Click.
    private void DiceVisualToggle_Toggled(object sender, RoutedEventArgs e)
    {
        bool on = DiceVisualToggle.IsChecked == true;
        if (_loading || on == SettingsService.DiceVisual)
        {
            return;
        }

        SettingsService.DiceVisual = on;

        // The tray rolls one at a time; dropping the count to 1 rerolls into it.
        if (SettingsService.DiceVisual && CountBox.Value > 1)
        {
            CountBox.Value = 1;
        }
        else
        {
            ShowResults(animate: true);
        }
    }

    private void DiceTray_RollRequested(object? sender, EventArgs e) => Generate(copy: SettingsService.AutoCopy);

    private void DiceTray_Settled(object? sender, EventArgs e) => RevealResult(animate: true);

    private void CopyButton_Click(object sender, RoutedEventArgs e) => CopyResults();

    // The window reads SettingsService.Pinned when focus leaves, so saving the flag is all it takes.
    private void PinButton_Toggled(object sender, RoutedEventArgs e)
    {
        UpdatePinGlyph();
        if (!_loading)
        {
            SettingsService.Pinned = PinButton.IsChecked == true;
        }
    }

    private void UpdatePinGlyph()
    {
        bool pinned = PinButton.IsChecked == true;
        PinGlyph.Glyph = pinned ? "" : "";
        string tip = pinned ? "Unpin (closes when you click away)" : "Keep open";
        AutomationProperties.SetName(PinButton, tip);
        ToolTipService.SetToolTip(PinButton, tip);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => App.Current.ShowSettings();
}
