using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using TrayRandom.Categories;
using TrayRandom.Draws;

namespace TrayRandom.ViewModels;

internal static class ListDrawPresentation
{
    private static readonly ResourceLoader Strings = new();

    public static string Get(string key) => Strings.GetString(key);

    public static string CategoryName(RandomCategory category) =>
        category.Group.UsesResourceNames ? Get(category.Name) : category.Name;

    public static string GroupName(CategoryGroup group) => group.UsesResourceNames ? Get(group.Name) : group.Name;

    public static string ModeName(DrawMode mode) => Get($"ListDrawMode{mode}");

    public static string ValidationMessage(DrawValidationException error) =>
        Format($"ListDrawError{error.Error}", error.LineNumber);

    public static string ListName(DrawRun run) => run.ListName ?? Get("ListDrawUntitled");

    public static string RunDetails(DrawRun run) => Format("ListDrawRunDetails", ModeName(run.Mode),
        run.RanAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));

    public static IReadOnlyList<GeneratedItem> ResultItems(DrawRun run) => run.Mode switch
    {
        DrawMode.Groups => [new GeneratedItem(ResultText(run))],
        _ => ResultLines(run, includeRanks: false).Select(text => new GeneratedItem(text)).ToArray(),
    };

    public static string ResultText(DrawRun run)
    {
        string separator = run.Mode == DrawMode.Groups ? Environment.NewLine + Environment.NewLine : Environment.NewLine;
        return string.Join(separator, ResultLines(run, includeRanks: true));
    }

    public static string Format(string key, params object?[] values) =>
        string.Format(CultureInfo.CurrentCulture, Get(key), values);

    private static IEnumerable<string> ResultLines(DrawRun run, bool includeRanks) => run.Mode switch
    {
        DrawMode.Ranking or DrawMode.WeightedRanking when includeRanks =>
            run.Lines.Select((line, i) => Format("ListDrawRank", i + 1, line.Entry)),
        DrawMode.Groups => run.Lines.GroupBy(line => line.Group).Select(group =>
            string.Join(Environment.NewLine, new[] { Format("ListDrawGroup", group.Key) }.Concat(group.Select(line => line.Entry)))),
        DrawMode.SecretSanta => run.Lines.Select(line => Format("ListDrawPair", line.Entry, line.Recipient)),
        _ => run.Lines.Select(line => line.Entry),
    };
}

public sealed record DrawHistoryItem(DrawRun Run)
{
    public string Title { get; } = ListDrawPresentation.ListName(Run);

    public string Details { get; } = ListDrawPresentation.RunDetails(Run);

    public override string ToString() => $"{Title}, {Details}";
}
