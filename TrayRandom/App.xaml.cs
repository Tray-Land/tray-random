using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TrayRandom.Categories;
using TrayRandom.Services;
using TrayRandom.Views;
using WinUIEx;

namespace TrayRandom;

/// <summary>
/// Tray-only application: no window at launch, just a notification-area icon that toggles the
/// flyout. Windows exist only while they're in use; the idle app is an icon and little else.
/// </summary>
public partial class App : Application
{
    public const string DisplayName = "Tray Random";

    private const string MutexName = "Local\\TrayRandom_SingleInstance";
    private const string ShowEventName = "Local\\TrayRandom_ShowFlyout";
    private const int MaxTooltipLength = 127; // NOTIFYICONDATA.szTip limit
    private const int MaxQuickCopyItems = 6;

    // Startup leaves JIT and first-request state behind that the idle app never touches again.
    private static readonly TimeSpan StartupTrimDelay = TimeSpan.FromSeconds(30);

    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private TrayIcon? _trayIcon;
    private TrayFlyoutWindow? _flyout;
    private DispatcherQueue? _dispatcher;
    private bool _isExiting;

    public App()
    {
        InitializeComponent();

        // A tray app lives on after its windows close; only the Exit menu ends it.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => LogCrash(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogCrash(e.ExceptionObject as Exception);
    }

    public static new App Current => (App)Application.Current;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            // Another instance owns the tray icon: ask it to open its flyout, then quit.
            try
            {
                // Let the running instance take the foreground when it shows its flyout.
                Windows.Win32.PInvoke.AllowSetForegroundWindow(unchecked((uint)-1)); // ASFW_ANY
                using EventWaitHandle existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch
            {
                // The other instance may be shutting down; nothing to do.
            }

            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Exit();
            return;
        }

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => _dispatcher.TryEnqueue(ShowFlyout), null, Timeout.Infinite, executeOnlyOnce: false);

        InitializeTrayIcon();

        // First run: show where the app lives instead of launching into silence.
        if (!SettingsService.HasLaunchedBefore)
        {
            SettingsService.HasLaunchedBefore = true;
            ShowFlyout();
        }
        else
        {
            MemoryService.ReleaseIdle(StartupTrimDelay);
        }
    }

    /// <summary>Sets the tray tooltip under the app name, e.g. "3 new" or null for just the name.</summary>
    public void SetTrayStatus(string? status)
    {
        if (_trayIcon is null)
        {
            return;
        }

        string tooltip = string.IsNullOrWhiteSpace(status) ? DisplayName : $"{DisplayName}\n{status}";
        _trayIcon.Tooltip = tooltip.Length > MaxTooltipLength ? tooltip[..MaxTooltipLength] : tooltip;
    }

    public void ShowFlyout()
    {
        TrayFlyoutWindow flyout = EnsureFlyout();
        flyout.HidePopup();
        flyout.ShowMainPage();
        flyout.ShowPopup();
    }

    /// <summary>Shows the flyout on its settings page.</summary>
    public void ShowSettings()
    {
        TrayFlyoutWindow flyout = EnsureFlyout();
        flyout.ShowSettingsPage();
        if (!flyout.IsPopupVisible)
        {
            flyout.ShowPopup();
        }
    }

    /// <summary>Generates one result for a favorite and copies it, without opening the flyout.</summary>
    private void QuickCopy(RandomCategory category)
    {
        string status;
        try
        {
            GeneratedItem item = new RandomContentGenerator(SettingsService.Locale).Generate(category, SettingsService.Inputs)[0];
            status = ClipboardService.TryCopy(item.Value) ? $"Copied {category.Name}: {item.Value.ReplaceLineEndings(" ")}" : "Couldn't copy; try again";
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            // Bad saved input (an empty list to pick from): open the flyout where it can be fixed.
            ShowFlyout();
            return;
        }

        SetTrayStatus(status);
    }

    private void InitializeTrayIcon()
    {
        _trayIcon = new TrayIcon(1, TrayIconPath, DisplayName);
        _trayIcon.Selected += (_, _) => EnsureFlyout().Toggle();
        _trayIcon.ContextMenu += TrayIcon_ContextMenu;
        _trayIcon.IsVisible = true;
        WindowPlacementService.SetTrayIcon(_trayIcon);
        SystemThemeService.Changed += (_, _) => _dispatcher?.TryEnqueue(() => _trayIcon?.SetIcon(TrayIconPath));
    }

    // A white glyph disappears on a light taskbar, which follows the Windows theme, not the app theme.
    private static string TrayIconPath => Path.Combine(
        AppContext.BaseDirectory, "Assets", SystemThemeService.IsLight ? "AppIcon-dark.ico" : "AppIcon.ico");

    private void TrayIcon_ContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        MenuFlyout menu = new();
        MenuFlyoutItem open = new() { Text = $"Open {DisplayName}", Icon = new FontIcon { Glyph = "" } };
        open.Click += (_, _) => ShowFlyout();
        menu.Items.Add(open);

        // Quick copies from the favorites, so common values are a right-click away.
        IReadOnlyList<string> favorites = SettingsService.Favorites;
        if (favorites.Count > 0)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            foreach (RandomCategory category in favorites.Take(MaxQuickCopyItems).Select(id => CategoryCatalog.Find(id)!))
            {
                MenuFlyoutItem item = new() { Text = $"Copy {category.Name.ToLower(CultureInfo.CurrentCulture)}", Icon = new FontIcon { Glyph = category.Glyph } };
                item.Click += (_, _) => QuickCopy(category);
                menu.Items.Add(item);
            }

            menu.Items.Add(new MenuFlyoutSeparator());
        }

        MenuFlyoutItem settings = new() { Text = "Settings", Icon = new FontIcon { Glyph = "" } };
        settings.Click += (_, _) => ShowSettings();
        MenuFlyoutItem exit = new() { Text = "Exit", Icon = new FontIcon { Glyph = "" } };
        exit.Click += (_, _) => ExitApp();

        menu.Items.Add(settings);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exit);
        args.Flyout = menu;
    }

    private TrayFlyoutWindow EnsureFlyout()
    {
        if (_flyout is null)
        {
            // The flyout closes itself after staying hidden for a while; the next open builds a new one.
            _flyout = new TrayFlyoutWindow();
            _flyout.Closed += (_, _) =>
            {
                _flyout = null;
                ReleaseIdleResourcesIfNoWindows();
            };
        }

        return _flyout;
    }

    private void ReleaseIdleResourcesIfNoWindows()
    {
        if (_flyout is null && !_isExiting)
        {
            MemoryService.ReleaseIdle();
        }
    }

    private void ExitApp()
    {
        _isExiting = true;
        _flyout?.CloseWindow();

        if (_trayIcon is not null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Exit();
    }

    private static void LogCrash(Exception? ex)
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "TrayRandom-crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:O}] {ex}{Environment.NewLine}");
        }
        catch
        {
            // Never throw from the crash logger.
        }
    }
}
