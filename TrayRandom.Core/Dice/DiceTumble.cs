using System.Numerics;

namespace TrayRandom.Dice;

/// <summary>
/// A die's pose at one moment of a throw, relative to where it comes to rest: lifted off the table
/// (<see cref="Offset"/> includes the height), enlarged while high, squashed on impact, flattened
/// as it tumbles, and spinning down to its resting tilt. The shadow stays on the table.
/// </summary>
public readonly record struct TumbleFrame(
    float Progress,
    Vector2 Offset,
    Vector2 Scale,
    float Rotation,
    Vector2 ShadowOffset,
    float ShadowScale,
    float ShadowOpacity);

/// <summary>
/// Samples a throw along a <see cref="DiceTrayLayout.ThrowPath"/>: a fall to the first bounce, a
/// smaller hop to the second, then a short skip and slide to rest. The frames become keyframes for
/// the die and its shadow.
/// </summary>
public static class DiceTumble
{
    /// <summary>Fraction of the throw when the die first hits the table.</summary>
    public const float FirstBounce = 0.4f;

    /// <summary>Fraction of the throw when it lands again; after this it only skips and slides.</summary>
    public const float SecondBounce = 0.7f;

    private const float LiftPerHeight = 0.9f;
    private const float GrowPerHeight = 0.45f;
    private const float Squash = 0.14f;
    private const float Flatten = 0.5f;
    private const int Tumbles = 3;

    public static TumbleFrame[] Sample(IReadOnlyList<Vector2> path, float restRotation, float spin, float dieSize, int frames)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(path.Count, 4);
        ArgumentOutOfRangeException.ThrowIfLessThan(frames, 2);

        Vector2 rest = path[3];
        TumbleFrame[] result = new TumbleFrame[frames];
        for (int i = 0; i < frames; i++)
        {
            float t = i / (float)(frames - 1);
            (Vector2 ground, float height) = Position(path, t);

            float impact = Impact(t, FirstBounce, 1f) + Impact(t, SecondBounce, 0.6f);
            float airborne = Math.Min(1, height * 4);
            float flatten = 1 - (Flatten * airborne * (0.5f - (0.5f * MathF.Cos(2 * MathF.PI * Tumbles * t))));
            float grow = 1 + (GrowPerHeight * height);
            float settle = (1 - t) * (1 - t) * (1 - t);

            result[i] = new TumbleFrame(
                t,
                ground - rest + new Vector2(0, -height * dieSize * LiftPerHeight),
                new Vector2(grow * (1 + (Squash * impact)) * flatten, grow * (1 - (Squash * impact))),
                restRotation + (spin * settle),
                ground - rest,
                1 - (0.35f * height),
                1 - (0.65f * height));
        }

        return result;
    }

    // Where on the table the die is over, and how high above it (1 at the throw, 0 on the table).
    private static (Vector2 Ground, float Height) Position(IReadOnlyList<Vector2> path, float t)
    {
        if (t < FirstBounce)
        {
            float s = t / FirstBounce;
            return (Vector2.Lerp(path[0], path[1], s), 1 - (s * s));
        }

        if (t < SecondBounce)
        {
            float s = (t - FirstBounce) / (SecondBounce - FirstBounce);
            return (Vector2.Lerp(path[1], path[2], s), 0.35f * 4 * s * (1 - s));
        }

        float r = (t - SecondBounce) / (1 - SecondBounce);
        float hop = Math.Min(1, r / 0.5f);
        float eased = 1 - ((1 - r) * (1 - r));
        return (Vector2.Lerp(path[2], path[3], eased), 0.08f * 4 * hop * (1 - hop));
    }

    // A short pulse centred on the moment of impact.
    private static float Impact(float t, float at, float strength)
    {
        float d = (t - at) / 0.03f;
        return strength * MathF.Exp(-d * d);
    }
}
