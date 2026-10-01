namespace TrayRandom.Categories;

/// <summary>Splits the user's list: one item per line, or comma-separated when it's all on one line.</summary>
public static class ListItems
{
    public static IReadOnlyList<string> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IEnumerable<string> items = lines.Length == 1
            ? lines[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : lines;
        return items.Where(s => s.Length > 0).ToArray();
    }
}
