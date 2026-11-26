using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TrayRandom.Draws;
using TrayRandom.Services;
using TrayRandom.ViewModels;

namespace TrayRandom.Views;

public sealed partial class FlyoutPage
{
    private readonly Lazy<DrawLibrary> _drawLibrary;
    private readonly Func<Task> _showDrawHistoryAsync;
    private DrawRun? _drawResult;
    private SavedDrawList? _selectedDrawList;
    private string _loadedDrawEntries = string.Empty;
    private ContentDialog? _drawDialog;
    private bool _drawListsLoaded;
    private bool _updatingDrawLists;

    internal bool IsDrawBusy { get; private set; }

    internal bool TryDismissDrawDialog()
    {
        if (_drawDialog is not { } dialog)
        {
            return false;
        }

        dialog.Hide();
        return true;
    }

    private void UpdateDrawControls()
    {
        DrawMode? mode = _category.DrawMode;
        bool isDraw = mode.HasValue;
        DrawInputsPanel.Visibility = isDraw ? Visibility.Visible : Visibility.Collapsed;
        GroupCountBox.Visibility = mode == DrawMode.Groups ? Visibility.Visible : Visibility.Collapsed;
        DrawWeightsHelp.Visibility = mode is DrawMode.WeightedWinner or DrawMode.WeightedRanking ? Visibility.Visible : Visibility.Collapsed;
        CountBox.Visibility = isDraw ? Visibility.Collapsed : Visibility.Visible;
        GenerateButton.IsEnabled = !isDraw || !IsDrawBusy;
        DrawEntriesBox.IsEnabled = !IsDrawBusy;
        RestoreDrawEntriesButton.IsEnabled = !IsDrawBusy;
        GroupCountBox.IsEnabled = !IsDrawBusy;
        SavedListsBox.IsEnabled = _drawListsLoaded && !IsDrawBusy;
        DrawOptionsButton.IsEnabled = !IsDrawBusy;
        DrawHistoryButton.IsEnabled = !IsDrawBusy;
        DeleteDrawListItem.IsEnabled = _selectedDrawList is not null;
    }

    private Task LoadDrawListsAsync() =>
        _drawListsLoaded ? Task.CompletedTask : ExecuteDrawActionAsync(_ => Task.CompletedTask);

    private async Task ExecuteDrawActionAsync(Func<DrawLibrary, Task> action, bool restoreGenerateFocus = false)
    {
        if (IsDrawBusy)
        {
            return;
        }

        // A draw may finish after the user switches category. Only update its result in place.
        var category = _category;
        bool loading = !_drawListsLoaded;
        IsDrawBusy = true;
        StatusBar.IsOpen = false;
        string? loadingText = loading && _isShown ? ListDrawPresentation.Get("ListDrawLoadingLists") : null;
        if (loadingText is not null)
        {
            _statusTimer.Stop();
            StatusText.Text = loadingText;
        }

        UpdateDrawControls();
        try
        {
            DrawLibrary library = _drawLibrary.Value;
            if (loading)
            {
                await Task.Run(library.LoadAsync);
                _drawListsLoaded = true;
                Guid? listId = SettingsService.LastDrawListId;
                SavedDrawList? list = library.Lists.FirstOrDefault(item => item.Id == listId);
                RefreshDrawLists(library, list);
            }

            await action(library);
        }
        catch (DrawValidationException error)
        {
            if (_category == category)
            {
                string message = error.Error == DrawError.EmptyList && _selectedDrawList is not null
                    ? ListDrawPresentation.Get("ListDrawErrorEmptySavedList")
                    : ListDrawPresentation.ValidationMessage(error);
                ShowDrawError(message, InfoBarSeverity.Warning);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (_category.DrawMode is not null)
            {
                ShowDrawError(ListDrawPresentation.Get(loading && !_drawListsLoaded ? "ListDrawLoadFailed" : "ListDrawSaveFailed"),
                    InfoBarSeverity.Error);
            }
        }
        finally
        {
            IsDrawBusy = false;
            if (loadingText is not null && StatusText.Text == loadingText)
            {
                StatusText.Text = string.Empty;
            }

            UpdateDrawControls();
            if (restoreGenerateFocus && _category == category && _isShown)
            {
                GenerateButton.Focus(FocusState.Programmatic);
            }
        }
    }

    private void ShowDrawError(string message, InfoBarSeverity severity)
    {
        StatusBar.Title = message;
        StatusBar.Message = string.Empty;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private void RefreshDrawLists(DrawLibrary library, SavedDrawList? selected)
    {
        _updatingDrawLists = true;
        SavedListsBox.ItemsSource = library.Lists;
        SavedListsBox.SelectedItem = selected;
        _updatingDrawLists = false;
        SetDrawList(selected);
    }

    private void SetDrawList(SavedDrawList? list)
    {
        _selectedDrawList = list;
        DrawEntriesBox.Text = list?.Entries ?? string.Empty;
        _loadedDrawEntries = DrawEntriesBox.Text;
        UpdateRestoreDrawEntriesButton();
        SettingsService.LastDrawListId = list?.Id;
        DeleteDrawListItem.IsEnabled = list is not null;
    }

    private bool HasModifiedSavedEntries =>
        _selectedDrawList is not null && !string.Equals(DrawEntriesBox.Text, _loadedDrawEntries, StringComparison.Ordinal);

    private void UpdateRestoreDrawEntriesButton() =>
        RestoreDrawEntriesButton.Visibility = HasModifiedSavedEntries ? Visibility.Visible : Visibility.Collapsed;

    private void ResetDrawResult()
    {
        StatusBar.IsOpen = false;
        _drawResult = null;
        _results = [];
        ShowResults();
    }

    private Task GenerateRequestedAsync()
    {
        if (_category.DrawMode is { } mode)
        {
            return RunDrawAsync(mode);
        }

        Generate(copy: SettingsService.AutoCopy);
        return Task.CompletedTask;
    }

    private Task RunDrawAsync(DrawMode mode)
    {
        double count = GroupCountBox.Value;
        int groupCount = count is >= 2 and <= int.MaxValue && double.IsInteger(count) ? (int)count : 0;
        return ExecuteDrawActionAsync(async library =>
        {
            if (_category.DrawMode != mode || !_isShown)
            {
                return;
            }

            string name = _selectedDrawList?.Name ?? string.Empty;
            string entries = DrawEntriesBox.Text;
            DrawRun run = await Task.Run(() => library.RunAsync(name, entries, mode, groupCount));
            if (_category.DrawMode != mode)
            {
                return;
            }

            _drawResult = run;
            _results = ListDrawPresentation.ResultItems(run);
            ShowResults(animate: _isShown);
            if (SettingsService.AutoCopy)
            {
                CopyResults();
            }
        }, restoreGenerateFocus: true);
    }

    private async Task<ContentDialogResult> ShowDrawDialogAsync(ContentDialog dialog)
    {
        dialog.XamlRoot = XamlRoot;
        _drawDialog = dialog;
        try
        {
            return await dialog.ShowAsync();
        }
        finally
        {
            _drawDialog = null;
        }
    }

    private void SavedListsBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_drawListsLoaded && !_updatingDrawLists)
        {
            SetDrawList(SavedListsBox.SelectedItem as SavedDrawList);
            ResetDrawResult();
        }
    }

    private void DrawEntriesBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateRestoreDrawEntriesButton();

    private async void RestoreDrawEntries_Click(object sender, RoutedEventArgs e)
    {
        if (IsDrawBusy || !HasModifiedSavedEntries || _selectedDrawList is not { } list)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(DrawEntriesBox.Text))
        {
            ContentDialog dialog = new()
            {
                Title = ListDrawPresentation.Get("ListDrawRestoreTitle"),
                Content = new TextBlock
                {
                    Text = ListDrawPresentation.Format("ListDrawRestorePrompt", list.Name),
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = ListDrawPresentation.Get("ListDrawRestoreAction"),
                CloseButtonText = ListDrawPresentation.Get("ListDrawCancel"),
                DefaultButton = ContentDialogButton.Close,
            };
            if (await ShowDrawDialogAsync(dialog) != ContentDialogResult.Primary ||
                !_isShown || _category.DrawMode is null || _selectedDrawList != list)
            {
                return;
            }
        }

        SetDrawList(list);
        ResetDrawResult();
        DrawEntriesBox.Focus(FocusState.Programmatic);
    }

    private void NewDrawList_Click(object sender, RoutedEventArgs e)
    {
        if (SavedListsBox.SelectedItem is not null)
        {
            SavedListsBox.SelectedItem = null;
        }
        else
        {
            SetDrawList(null);
            ResetDrawResult();
        }

        DrawEntriesBox.Focus(FocusState.Programmatic);
    }

    private async void SaveDrawList_Click(object sender, RoutedEventArgs e) => await ExecuteDrawActionAsync(async library =>
    {
        if (!_isShown || _category.DrawMode is null)
        {
            return;
        }

        string label = ListDrawPresentation.Get("ListDrawNameLabel");
        TextBox nameBox = new() { Text = _selectedDrawList?.Name ?? string.Empty, Header = label };
        AutomationProperties.SetName(nameBox, label);
        ContentDialog dialog = new()
        {
            Title = ListDrawPresentation.Get("ListDrawSaveTitle"),
            Content = nameBox,
            PrimaryButtonText = ListDrawPresentation.Get("ListDrawSaveTitle"),
            CloseButtonText = ListDrawPresentation.Get("ListDrawCancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await ShowDrawDialogAsync(dialog) == ContentDialogResult.Primary)
        {
            SavedDrawList saved = await library.SaveListAsync(_selectedDrawList?.Id, nameBox.Text, DrawEntriesBox.Text);
            RefreshDrawLists(library, saved);
            ShowStatus(ListDrawPresentation.Get("ListDrawSaved"));
        }
    });

    private async void DeleteDrawList_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDrawList is not { } list)
        {
            return;
        }

        await ExecuteDrawActionAsync(async library =>
        {
            ContentDialog dialog = new()
            {
                Title = ListDrawPresentation.Get("ListDrawDeleteListTitle"),
                Content = new TextBlock
                {
                    Text = ListDrawPresentation.Format("ListDrawDeleteListPrompt", list.Name),
                    TextWrapping = TextWrapping.Wrap,
                },
                PrimaryButtonText = ListDrawPresentation.Get("ListDrawDelete"),
                CloseButtonText = ListDrawPresentation.Get("ListDrawCancel"),
            };
            if (await ShowDrawDialogAsync(dialog) == ContentDialogResult.Primary)
            {
                await library.DeleteListAsync(list.Id);
                RefreshDrawLists(library, null);
                ShowStatus(ListDrawPresentation.Get("ListDrawListDeleted"));
            }
        });
    }

    private async void DrawHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!IsDrawBusy)
        {
            await _showDrawHistoryAsync();
        }
    }
}
