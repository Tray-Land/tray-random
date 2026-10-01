namespace TrayRandom.Favorites;

/// <summary>
/// The ordered list of favorite category ids. Pure list logic; the app persists the result.
/// Every method returns a new list and leaves its input alone.
/// </summary>
public static class FavoritesList
{
    public const int MaxFavorites = 24;

    /// <summary>Drops ids that no longer exist and duplicates, keeping the user's order.</summary>
    public static IReadOnlyList<string> Sanitize(IEnumerable<string>? ids, Func<string, bool> exists) =>
        (ids ?? []).Where(id => !string.IsNullOrEmpty(id) && exists(id)).Distinct(StringComparer.Ordinal).Take(MaxFavorites).ToArray();

    /// <summary>Adds the id at the end, or removes it if it's already a favorite.</summary>
    public static IReadOnlyList<string> Toggle(IReadOnlyList<string> favorites, string id)
    {
        if (favorites.Contains(id, StringComparer.Ordinal))
        {
            return favorites.Where(f => f != id).ToArray();
        }

        return favorites.Count >= MaxFavorites ? favorites : [.. favorites, id];
    }

    /// <summary>Moves the id one step toward the start (<paramref name="offset"/> -1) or end (+1).</summary>
    public static IReadOnlyList<string> Move(IReadOnlyList<string> favorites, string id, int offset)
    {
        int from = favorites.ToList().IndexOf(id);
        if (from < 0)
        {
            return favorites;
        }

        int to = Math.Clamp(from + offset, 0, favorites.Count - 1);
        List<string> list = [.. favorites];
        list.RemoveAt(from);
        list.Insert(to, id);
        return list;
    }

    /// <summary>The favorite after or before the current one, wrapping around; null with no favorites.</summary>
    public static string? Cycle(IReadOnlyList<string> favorites, string? current, int direction)
    {
        if (favorites.Count == 0)
        {
            return null;
        }

        int index = current is null ? -1 : favorites.ToList().IndexOf(current);
        if (index < 0)
        {
            return direction >= 0 ? favorites[0] : favorites[^1];
        }

        return favorites[((index + direction) % favorites.Count + favorites.Count) % favorites.Count];
    }
}
