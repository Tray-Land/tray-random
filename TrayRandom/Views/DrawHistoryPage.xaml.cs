using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrayRandom.Draws;
using TrayRandom.Services;
using TrayRandom.ViewModels;

namespace TrayRandom.Views;

public sealed partial class DrawHistoryPage : Page
{
    private readonly Lazy<DrawLibrary> _library;
    private readonly Action _goBack;
    private DrawRun? _selectedRun;
    private bool _isLoaded;

    internal DrawHistoryPage(Lazy<DrawLibrary> library, Action goBack)
    {
        _library = library;
        _goBack = goBack;
        InitializeComponent();
    }

    internal bool IsBusy { get; private set; }

    internal Task OpenAsync()
    {
        if (IsBusy)
        {
            return Task.CompletedTask;
        }

        _selectedRun = null;
        return LoadAsync();
    }

    internal void GoBack()
    {
        if (IsBusy)
        {
            return;
        }

        if (_selectedRun is not null)
        {
            _selectedRun = null;
            StatusBar.IsOpen = false;
            UpdateView();
            FocusCurrentView();
        }
        else
        {
            _goBack();
        }
    }

    private Task LoadAsync() => ExecuteAsync(async () =>
    {
        await Task.Run(_library.Value.LoadAsync);
        RefreshHistory();
        _isLoaded = true;
    }, "ListDrawLoadFailed");

    private async Task ExecuteAsync(Func<Task> action, string errorKey)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusBar.IsOpen = false;
        UpdateView();
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ShowMessage(errorKey, InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            UpdateView();
            FocusCurrentView();
        }
    }

    private void RefreshHistory() =>
        HistoryList.ItemsSource = _library.Value.Runs.Select(run => new DrawHistoryItem(run)).ToArray();

    private void UpdateView()
    {
        bool hasResult = _selectedRun is not null;
        bool hasHistory = _isLoaded && _library.Value.Runs.Count > 0;
        PageTitle.Text = ListDrawPresentation.Get(hasResult ? "ListDrawResultTitle" : "ListDrawHistoryTitle");
        BackButton.IsEnabled = !IsBusy;
        HistoryCard.Visibility = _isLoaded && !hasResult ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.Visibility = hasHistory ? Visibility.Visible : Visibility.Collapsed;
        HistoryList.IsEnabled = !IsBusy;
        EmptyHistoryText.Visibility = hasHistory ? Visibility.Collapsed : Visibility.Visible;
        ResultCard.Visibility = hasResult ? Visibility.Visible : Visibility.Collapsed;
        ResultActions.Visibility = ResultCard.Visibility;
        DeleteRunButton.IsEnabled = !IsBusy;
        CopyButton.IsEnabled = !IsBusy;
        LoadingText.Visibility = !_isLoaded && IsBusy ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = !_isLoaded && !IsBusy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void FocusCurrentView()
    {
        if (IsBusy)
        {
            return;
        }

        Control target = _selectedRun is not null ? CopyButton
            : !_isLoaded ? RetryButton
            : _library.Value.Runs.Count > 0 ? HistoryList
            : BackButton;
        target.Focus(FocusState.Programmatic);
    }

    private void ShowMessage(string key, InfoBarSeverity severity = InfoBarSeverity.Success)
    {
        StatusBar.Title = ListDrawPresentation.Get(key);
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    private void Page_Loaded(object sender, RoutedEventArgs e) => FocusCurrentView();

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    private async void Retry_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void HistoryList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is DrawHistoryItem item)
        {
            _selectedRun = item.Run;
            ResultTitle.Text = item.Title;
            ResultDetails.Text = item.Details;
            ResultText.Text = ListDrawPresentation.ResultText(item.Run);
            StatusBar.IsOpen = false;
            UpdateView();
            FocusCurrentView();
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRun is null)
        {
            return;
        }

        if (ClipboardService.TryCopy(ResultText.Text))
        {
            ShowMessage("ListDrawCopied");
        }
        else
        {
            ShowMessage("ListDrawCopyFailed", InfoBarSeverity.Warning);
        }
    }

    private async void DeleteRun_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedRun is { } run)
        {
            await ExecuteAsync(async () =>
            {
                await _library.Value.DeleteRunAsync(run.Id);
                _selectedRun = null;
                RefreshHistory();
                ShowMessage("ListDrawRunDeleted");
            }, "ListDrawSaveFailed");
        }
    }
}
