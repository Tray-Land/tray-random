using System.ComponentModel;
using Microsoft.UI.Xaml;
using TrayRandom.Categories;
using TrayRandom.Text;
using TrayRandom.ViewModels;

namespace TrayRandom.Views;

/// <summary>A favorite category in the strip across the top of the flyout.</summary>
public sealed class FavoriteChip(RandomCategory category, int position)
{
    public RandomCategory Category { get; } = category;

    public string Id => Category.Id;

    public string Name { get; } = ListDrawPresentation.CategoryName(category);

    public string Glyph => Category.Glyph;

    // Ctrl+1..9 shortcuts only reach the first nine.
    public string ToolTip { get; } = position <= 9
        ? $"{ListDrawPresentation.GroupName(category.Group)} (Ctrl+{position})"
        : ListDrawPresentation.GroupName(category.Group);
}

/// <summary>One line in the results list when generating several at once.</summary>
public sealed class ResultRow(GeneratedItem item, int number) : INotifyPropertyChanged
{
    private string _value = item.Value;
    private string _detail = item.Detail ?? string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Number { get; } = number.ToString(System.Globalization.CultureInfo.CurrentCulture);

    /// <summary>What the row shows right now: scrambled mid-animation, otherwise the real value.</summary>
    public string Value => _value;

    public string Detail => _detail;

    /// <summary>The real value, for automation and copying.</summary>
    public string FinalValue => item.Value;

    public Visibility DetailVisibility => string.IsNullOrEmpty(item.Detail) ? Visibility.Collapsed : Visibility.Visible;

    public void Scramble(double progress, Random random) => Show(
        TextScramble.Frame(item.Value, progress, random),
        TextScramble.Frame(item.Detail ?? string.Empty, progress, random));

    public void Settle() => Show(item.Value, item.Detail ?? string.Empty);

    private void Show(string value, string detail)
    {
        _value = value;
        _detail = detail;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
    }
}
