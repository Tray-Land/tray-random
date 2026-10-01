using Microsoft.UI.Xaml;
using TrayRandom.Categories;

namespace TrayRandom.Views;

/// <summary>A favorite category in the strip across the top of the flyout.</summary>
public sealed class FavoriteChip(RandomCategory category, int position)
{
    public RandomCategory Category { get; } = category;

    public string Id => Category.Id;

    public string Name => Category.Name;

    public string Glyph => Category.Glyph;

    // Ctrl+1..9 shortcuts only reach the first nine.
    public string ToolTip => position <= 9 ? $"{Category.Group.Name} (Ctrl+{position})" : Category.Group.Name;
}

/// <summary>One line in the results list when generating several at once.</summary>
public sealed class ResultRow(GeneratedItem item, int number)
{
    public string Number { get; } = number.ToString(System.Globalization.CultureInfo.CurrentCulture);

    public string Value => item.Value;

    public string Detail => item.Detail ?? string.Empty;

    public Visibility DetailVisibility => string.IsNullOrEmpty(item.Detail) ? Visibility.Collapsed : Visibility.Visible;
}
