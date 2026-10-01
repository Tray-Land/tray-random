using Bogus;
using TrayRandom.Dice;

namespace TrayRandom.Categories;

/// <summary>Extra input a category needs before it can generate, shown under the picker.</summary>
public enum CategoryInput
{
    None,
    Dice,
    Range,
    List,
}

/// <summary>One kind of random thing the app can make, e.g. "Email" in the Internet group.</summary>
/// <param name="Id">Stable key saved in favorites and settings; never rename one.</param>
/// <param name="Generate">Makes one result. Categories that must not repeat across a batch (picking
/// raffle winners) set <see cref="GenerateBatch"/> instead.</param>
public sealed record RandomCategory(
    string Id,
    string Name,
    CategoryGroup Group,
    Func<GenerationContext, GeneratedItem>? Generate,
    CategoryInput Input = CategoryInput.None,
    Func<GenerationContext, int, IReadOnlyList<GeneratedItem>>? GenerateBatch = null)
{
    public string Glyph => Group.Glyph;

    /// <summary>Verb for the main button: dice are rolled, coins flipped, everything else generated.</summary>
    public string ActionVerb => Id switch
    {
        "dice" => "Roll",
        "coin" => "Flip",
        "card" => "Draw",
        "pick" => "Pick",
        "shuffle" => "Shuffle",
        _ => "Generate",
    };
}

public sealed record CategoryGroup(string Name, string Glyph);

/// <summary>A result: <see cref="Value"/> is what gets copied; <see cref="Detail"/> is optional
/// working shown beneath it (the individual dice of a roll).</summary>
public sealed record GeneratedItem(string Value, string? Detail = null)
{
    /// <summary>The roll behind a dice result, so the flyout can draw the dice.</summary>
    public DiceRoll? Roll { get; init; }

    public static implicit operator GeneratedItem(string value) => new(value);
}

/// <summary>The user's inputs for the categories that take them. Saved between sessions.</summary>
public sealed record CategoryInputs
{
    public string DiceNotation { get; init; } = "1d20";

    public long RangeMin { get; init; } = 1;

    public long RangeMax { get; init; } = 100;

    public string ListText { get; init; } = string.Empty;
}

public sealed class GenerationContext(Faker faker, CategoryInputs inputs)
{
    public Faker Faker { get; } = faker;

    public Randomizer Random => Faker.Random;

    public CategoryInputs Inputs { get; } = inputs;
}
