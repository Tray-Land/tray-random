using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;
using TrayRandom.Categories;
using TrayRandom.Favorites;
using Windows.Storage;

namespace TrayRandom.Services;

/// <summary>
/// Typed wrapper over <see cref="ApplicationData.LocalSettings"/>. Every read falls back to a
/// default so a missing or corrupt value never crashes the app. Not for secrets: those belong in
/// a file or PasswordVault.
/// </summary>
/// <remarks>
/// LocalSettings values must be small (8 KB each) WinRT primitives. Store enums as int, and lists
/// or records as JSON through a source-generated <c>JsonSerializerContext</c> (trim-safe) with
/// <see cref="GetJson"/> / <see cref="SetJson"/>. Anything larger belongs in a file under
/// <c>ApplicationData.Current.LocalFolder</c>.
/// </remarks>
internal static class SettingsService
{
    /// <summary>False until the first launch has shown the flyout.</summary>
    public static bool HasLaunchedBefore
    {
        get => Get(nameof(HasLaunchedBefore), false);
        set => Set(nameof(HasLaunchedBefore), value);
    }

    /// <summary>Raised on the UI thread after Settings changes something the flyout shows.</summary>
    public static event EventHandler? Changed;

    /// <summary>Bogus locale code for generated names, addresses, and text.</summary>
    public static string Locale
    {
        get => Locales.Resolve(Get<string?>(nameof(Locale), null));
        set
        {
            Set(nameof(Locale), value);
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>Copy each new result to the clipboard as soon as it's generated.</summary>
    public static bool AutoCopy
    {
        get => Get(nameof(AutoCopy), false);
        set => Set(nameof(AutoCopy), value);
    }

    /// <summary>Roll animated dice in a tray instead of showing only the total.</summary>
    public static bool DiceVisual
    {
        get => Get(nameof(DiceVisual), true);
        set => Set(nameof(DiceVisual), value);
    }

    /// <summary>Briefly scramble new text as it settles in (about 400 ms).</summary>
    public static bool TextAnimation
    {
        get => Get(nameof(TextAnimation), true);
        set => Set(nameof(TextAnimation), value);
    }

    /// <summary>Keep the flyout open when focus leaves it; only the tray icon or Alt+F4 hides it.</summary>
    public static bool Pinned
    {
        get => Get(nameof(Pinned), false);
        set => Set(nameof(Pinned), value);
    }

    /// <summary>Favorite category ids in the user's order. Unknown ids are dropped on read.</summary>
    public static IReadOnlyList<string> Favorites
    {
        get => Get<string?>(nameof(Favorites), null) is null
            ? CategoryCatalog.DefaultFavorites
            : FavoritesList.Sanitize(GetJson(nameof(Favorites), SettingsJsonContext.Default.StringArray), CategoryCatalog.Exists);
        set => SetJson(nameof(Favorites), value.ToArray(), SettingsJsonContext.Default.StringArray);
    }

    public static string LastCategoryId
    {
        get => Get<string?>(nameof(LastCategoryId), null) is { } id && CategoryCatalog.Exists(id) ? id : CategoryCatalog.DefaultCategoryId;
        set => Set(nameof(LastCategoryId), value);
    }

    /// <summary>How many results to make at once.</summary>
    public static int Count
    {
        get => Math.Clamp(Get(nameof(Count), 1), 1, RandomContentGenerator.MaxCount);
        set => Set(nameof(Count), value);
    }

    public static CategoryInputs Inputs
    {
        get => GetJson(nameof(Inputs), SettingsJsonContext.Default.CategoryInputs) ?? new CategoryInputs();
        set => SetJson(nameof(Inputs), value, SettingsJsonContext.Default.CategoryInputs);
    }

    public static T? GetJson<T>(string key, JsonTypeInfo<T> typeInfo)
    {
        if (Get<string?>(key, null) is not { } json)
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public static void SetJson<T>(string key, T? value, JsonTypeInfo<T> typeInfo) =>
        Set(key, value is null ? null : JsonSerializer.Serialize(value, typeInfo));

    private static T Get<T>(string key, T defaultValue)
    {
        try
        {
            return ApplicationData.Current.LocalSettings.Values.TryGetValue(key, out object? value) && value is T typed
                ? typed
                : defaultValue;
        }
        catch
        {
            return defaultValue;
        }
    }

    private static void Set(string key, object? value)
    {
        try
        {
            if (value is null)
            {
                ApplicationData.Current.LocalSettings.Values.Remove(key);
            }
            else
            {
                ApplicationData.Current.LocalSettings.Values[key] = value;
            }
        }
        catch
        {
            // Settings are best-effort; ignore storage failures.
        }
    }
}

[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(CategoryInputs))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
