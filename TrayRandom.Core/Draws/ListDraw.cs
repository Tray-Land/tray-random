using System.Globalization;

namespace TrayRandom.Draws;

public enum DrawMode
{
    Winner,
    Ranking,
    WeightedWinner,
    WeightedRanking,
    Groups,
    SecretSanta,
}

public enum DrawError
{
    EmptyList,
    MissingListName,
    DuplicateListName,
    InvalidWeight,
    NoPositiveWeights,
    InvalidGroupCount,
    NotEnoughForSecretSanta,
    DuplicateSecretSantaEntry,
}

public sealed class DrawValidationException(DrawError error, int lineNumber = 0) : Exception(error.ToString())
{
    public DrawError Error { get; } = error;

    public int LineNumber { get; } = lineNumber;
}

public sealed record DrawLine(string Entry, int? Group = null, string? Recipient = null);

public static class ListDraw
{
    public static DrawLine[] Run(string text, DrawMode mode, int groupCount = 0, Random? random = null)
    {
        var entries = ParseEntries(text);
        random ??= Random.Shared;
        if (mode is DrawMode.WeightedWinner or DrawMode.WeightedRanking)
        {
            return RunWeighted(entries, mode, random);
        }

        string[] names = entries.Select(entry => entry.Name).ToArray();
        random.Shuffle(names);
        return mode switch
        {
            DrawMode.Winner => [new(names[0])],
            DrawMode.Ranking => Array.ConvertAll(names, name => new DrawLine(name)),
            DrawMode.Groups => SplitGroups(names, groupCount),
            DrawMode.SecretSanta => AssignSecretSanta(names),
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
    }

    internal static Entry[] ParseEntries(string text)
    {
        List<Entry> entries = [];
        using StringReader reader = new(text);
        for (int lineNumber = 1; reader.ReadLine() is { } line; lineNumber++)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            int separator = line.LastIndexOf('|');
            string name = (separator < 0 ? line : line[..separator]).Trim();
            int weight = 1;
            if (name.Length == 0 || (separator >= 0 &&
                !int.TryParse(line.AsSpan(separator + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out weight)))
            {
                throw new DrawValidationException(DrawError.InvalidWeight, lineNumber);
            }

            entries.Add(new Entry(name, weight));
        }

        return entries.Count == 0
            ? throw new DrawValidationException(DrawError.EmptyList)
            : entries.ToArray();
    }

    private static DrawLine[] RunWeighted(Entry[] entries, DrawMode mode, Random random)
    {
        // Independent exponential times give weighted selection without replacement or extra tickets.
        var ordered = entries.Where(entry => entry.Weight > 0)
            .OrderBy(entry => -Math.Log(1 - random.NextDouble()) / entry.Weight);
        var selected = mode == DrawMode.WeightedWinner ? ordered.Take(1) : ordered;
        DrawLine[] lines = selected.Select(entry => new DrawLine(entry.Name)).ToArray();
        return lines.Length == 0 ? throw new DrawValidationException(DrawError.NoPositiveWeights) : lines;
    }

    private static DrawLine[] SplitGroups(string[] names, int groupCount)
    {
        if (groupCount < 2 || groupCount > names.Length)
        {
            throw new DrawValidationException(DrawError.InvalidGroupCount);
        }

        int smallerSize = Math.DivRem(names.Length, groupCount, out int largerGroups);
        DrawLine[] lines = new DrawLine[names.Length];
        int index = 0;
        for (int group = 1; group <= groupCount; group++)
        {
            int size = smallerSize + (group <= largerGroups ? 1 : 0);
            for (int i = 0; i < size; i++, index++)
            {
                lines[index] = new DrawLine(names[index], Group: group);
            }
        }

        return lines;
    }

    private static DrawLine[] AssignSecretSanta(string[] names)
    {
        if (names.Length < 2)
        {
            throw new DrawValidationException(DrawError.NotEnoughForSecretSanta);
        }

        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
        {
            throw new DrawValidationException(DrawError.DuplicateSecretSantaEntry);
        }

        // A shuffled cycle gives everyone one recipient without assigning anyone to themselves.
        return names.Select((name, index) => new DrawLine(name, Recipient: names[(index + 1) % names.Length])).ToArray();
    }

    internal readonly record struct Entry(string Name, int Weight);
}
