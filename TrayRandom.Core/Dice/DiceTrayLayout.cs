using System.Numerics;

namespace TrayRandom.Dice;

/// <summary>One die as the dice tray draws it.</summary>
/// <param name="Subtracted">The die belongs to a term that's taken away, like the d4 in 2d6-d4.</param>
public readonly record struct TrayDie(int Sides, int Value, bool Kept, bool Subtracted)
{
    /// <summary>Rolled the highest face and counts toward the total: a natural 20 and the like.</summary>
    public bool IsCritical => Kept && Value == Sides && Sides >= 4;
}

/// <summary>Where a die comes to rest, by its center, and how far it ends up tilted (degrees).</summary>
public readonly record struct DiePlacement(Vector2 Center, float Rotation);

/// <summary>The die size for a roll and each die's resting place.</summary>
public sealed record TrayArrangement(float DieSize, IReadOnlyList<DiePlacement> Placements);

/// <summary>The side of the tray the dice are thrown in from.</summary>
public enum ThrowSide
{
    Left,
    Top,
    Right,
    Bottom,
}

/// <summary>
/// Lays dice out in a tray: one scattered cell per die, so none overlap however they are tilted,
/// and the path each one takes from outside the tray to its resting place.
/// </summary>
public static class DiceTrayLayout
{
    /// <summary>The most dice the tray draws; a bigger roll shows the first ones and a count.</summary>
    public const int MaxDice = 24;

    public const float MaxDieSize = 72;

    public const float MaxTilt = 18;

    // A square tilted up to MaxTilt degrees fits in a square this many times its side
    // (cos 18° + sin 18° ≈ 1.26), with a little room to spare.
    public const float Footprint = 1.3f;

    private const float DieToCell = 0.62f;

    /// <summary>Every die in the roll, in order; constants have no dice and are skipped.</summary>
    public static IReadOnlyList<TrayDie> Flatten(DiceRoll roll) =>
        roll.Terms
            .Where(t => t.Term.IsDice)
            .SelectMany(t => t.Dice.Select(d => new TrayDie(t.Term.Sides, d.Value, d.Kept, t.Term.Sign < 0)))
            .ToArray();

    /// <summary>
    /// Picks the grid with the biggest cells for <paramref name="count"/> dice in the tray, then
    /// places each die somewhere random in its cell, tilted up to <see cref="MaxTilt"/> degrees.
    /// </summary>
    public static TrayArrangement Arrange(int count, float width, float height, Random random)
    {
        if (count <= 0 || width <= 0 || height <= 0)
        {
            return new TrayArrangement(0, []);
        }

        int columns = 1;
        float cell = 0;
        for (int c = 1; c <= count; c++)
        {
            int r = (count + c - 1) / c;
            float size = Math.Min(width / c, height / r);
            if (size > cell)
            {
                cell = size;
                columns = c;
            }
        }

        int rows = (count + columns - 1) / columns;
        float dieSize = Math.Min(MaxDieSize, cell * DieToCell);
        float slack = Math.Min(dieSize * 0.5f, Math.Max(0, (cell - (dieSize * Footprint)) / 2));
        float top = (height - (rows * cell)) / 2;

        DiePlacement[] placements = new DiePlacement[count];
        for (int i = 0; i < count; i++)
        {
            int row = i / columns;
            int column = i % columns;
            int inRow = row == rows - 1 ? count - (row * columns) : columns;
            float left = (width - (inRow * cell)) / 2;
            Vector2 center = new(
                left + ((column + 0.5f) * cell) + Jitter(random, slack),
                top + ((row + 0.5f) * cell) + Jitter(random, slack));
            placements[i] = new DiePlacement(center, Jitter(random, MaxTilt));
        }

        return new TrayArrangement(dieSize, placements);
    }

    /// <summary>
    /// The ground track of a throw: a start point well outside the tray on <paramref name="side"/>,
    /// a first bounce partway in, a second just past the resting place, and the resting place.
    /// Both bounces stay inside the tray.
    /// </summary>
    public static Vector2[] ThrowPath(Vector2 rest, ThrowSide side, float width, float height, float dieSize, Random random)
    {
        // Far enough out that the die, drawn lifted and enlarged at the start, is still unseen.
        float margin = dieSize * 1.9f;
        Vector2 start = side switch
        {
            ThrowSide.Left => new(-margin, Between(random, height * 0.2f, height * 0.8f)),
            ThrowSide.Right => new(width + margin, Between(random, height * 0.2f, height * 0.8f)),
            ThrowSide.Top => new(Between(random, width * 0.2f, width * 0.8f), -margin),
            _ => new(Between(random, width * 0.2f, width * 0.8f), height + margin),
        };

        Vector2 travel = rest - start;
        Vector2 direction = travel.LengthSquared() > 0 ? Vector2.Normalize(travel) : Vector2.UnitX;
        Vector2 across = new(-direction.Y, direction.X);

        Vector2 first = start + (travel * 0.55f) + (across * travel.Length() * Jitter(random, 0.18f));
        Vector2 second = rest + (direction * dieSize * Between(random, 0.15f, 0.35f)) + (across * dieSize * Jitter(random, 0.1f));

        return [start, Inside(first, width, height, dieSize), Inside(second, width, height, dieSize), rest];
    }

    private static Vector2 Inside(Vector2 point, float width, float height, float dieSize)
    {
        float half = dieSize / 2;
        return new Vector2(
            width > dieSize ? Math.Clamp(point.X, half, width - half) : width / 2,
            height > dieSize ? Math.Clamp(point.Y, half, height - half) : height / 2);
    }

    private static float Jitter(Random random, float amount) => ((random.NextSingle() * 2) - 1) * amount;

    private static float Between(Random random, float min, float max) => min + (random.NextSingle() * (max - min));
}
