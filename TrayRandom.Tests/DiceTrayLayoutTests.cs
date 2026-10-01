using System.Numerics;
using TrayRandom.Dice;

namespace TrayRandom.Tests;

[TestClass]
public sealed class DiceTrayLayoutTests
{
    private const float Tolerance = 0.01f;

    [TestMethod]
    public void Flatten_SkipsConstantsAndMarksSubtractedDice()
    {
        DiceRoll roll = DiceExpression.Parse("3d6kh2-d4+5").Roll(new Random(7));

        IReadOnlyList<TrayDie> dice = DiceTrayLayout.Flatten(roll);

        Assert.AreEqual(4, dice.Count);
        Assert.IsTrue(dice.Take(3).All(d => d.Sides == 6 && !d.Subtracted));
        Assert.AreEqual(2, dice.Take(3).Count(d => d.Kept));
        Assert.IsTrue(dice[3] is { Sides: 4, Subtracted: true, Kept: true });
    }

    [TestMethod]
    [DataRow(20, 20, true, true)]
    [DataRow(20, 19, true, false)]
    [DataRow(20, 20, false, false)]
    [DataRow(2, 2, true, false)]
    public void IsCritical_OnlyForKeptTopFace(int sides, int value, bool kept, bool expected) =>
        Assert.AreEqual(expected, new TrayDie(sides, value, kept, false).IsCritical);

    [TestMethod]
    [DataRow(370f, 180f)]
    [DataRow(370f, 300f)]
    [DataRow(120f, 400f)]
    public void Arrange_KeepsEveryDieInsideTheTrayWithoutOverlap(float width, float height)
    {
        Random random = new(42);
        for (int count = 1; count <= DiceTrayLayout.MaxDice; count++)
        {
            TrayArrangement layout = DiceTrayLayout.Arrange(count, width, height, random);
            float half = layout.DieSize * DiceTrayLayout.Footprint / 2;

            Assert.AreEqual(count, layout.Placements.Count);
            Assert.IsTrue(layout.DieSize > 0 && layout.DieSize <= DiceTrayLayout.MaxDieSize);
            foreach (DiePlacement p in layout.Placements)
            {
                Assert.IsTrue(p.Center.X - half >= -Tolerance && p.Center.X + half <= width + Tolerance, $"{count} dice: x {p.Center.X}");
                Assert.IsTrue(p.Center.Y - half >= -Tolerance && p.Center.Y + half <= height + Tolerance, $"{count} dice: y {p.Center.Y}");
                Assert.IsTrue(Math.Abs(p.Rotation) <= DiceTrayLayout.MaxTilt);
            }

            for (int a = 0; a < count; a++)
            {
                for (int b = a + 1; b < count; b++)
                {
                    Vector2 gap = Vector2.Abs(layout.Placements[a].Center - layout.Placements[b].Center);
                    Assert.IsTrue(gap.X >= (2 * half) - Tolerance || gap.Y >= (2 * half) - Tolerance, $"{count} dice: {a} overlaps {b}");
                }
            }
        }
    }

    [TestMethod]
    public void Arrange_CapsTheDieSize() =>
        Assert.AreEqual(DiceTrayLayout.MaxDieSize, DiceTrayLayout.Arrange(1, 1000, 1000, new Random(1)).DieSize);

    [TestMethod]
    [DataRow(0, 100f, 100f)]
    [DataRow(3, 0f, 100f)]
    [DataRow(3, 100f, 0f)]
    public void Arrange_IsEmptyWithNothingToPlace(int count, float width, float height) =>
        Assert.AreEqual(0, DiceTrayLayout.Arrange(count, width, height, new Random(1)).Placements.Count);

    [TestMethod]
    public void ThrowPath_StartsOutsideBouncesInsideAndEndsAtRest()
    {
        const float Width = 370, Height = 200, Size = 56;
        Random random = new(3);
        Vector2 rest = new(Width / 2, Height / 2);
        foreach (ThrowSide side in Enum.GetValues<ThrowSide>())
        {
            Vector2[] path = DiceTrayLayout.ThrowPath(rest, side, Width, Height, Size, random);

            Assert.AreEqual(4, path.Length);
            Assert.IsTrue(path[0].X < 0 || path[0].X > Width || path[0].Y < 0 || path[0].Y > Height, $"{side} starts inside");
            foreach (Vector2 bounce in path[1..3])
            {
                Assert.IsTrue(bounce.X >= Size / 2 && bounce.X <= Width - (Size / 2), $"{side}: x {bounce.X}");
                Assert.IsTrue(bounce.Y >= Size / 2 && bounce.Y <= Height - (Size / 2), $"{side}: y {bounce.Y}");
            }

            Assert.AreEqual(rest, path[3]);
        }
    }

    [TestMethod]
    public void Tumble_StartsAtTheThrowAndSettlesAtRest()
    {
        Vector2[] path = [new(-100, 50), new(80, 60), new(140, 90), new(120, 80)];

        TumbleFrame[] frames = DiceTumble.Sample(path, restRotation: 12, spin: 720, dieSize: 56, frames: 40);

        TumbleFrame first = frames[0], last = frames[^1];
        Assert.AreEqual(path[0] - path[3], first.ShadowOffset);
        Assert.IsTrue(first.Offset.Y < first.ShadowOffset.Y, "Starts in the air above its shadow");
        Assert.IsTrue(first.Scale.Y > 1, "Starts enlarged, nearer the viewer");
        Assert.AreEqual(12 + 720, first.Rotation, Tolerance);

        Assert.AreEqual(0, last.Offset.Length(), Tolerance);
        Assert.AreEqual(0, last.ShadowOffset.Length(), Tolerance);
        Assert.AreEqual(1, last.Scale.X, Tolerance);
        Assert.AreEqual(1, last.Scale.Y, Tolerance);
        Assert.AreEqual(12, last.Rotation, Tolerance);
        Assert.AreEqual(1, last.ShadowOpacity, Tolerance);
    }

    [TestMethod]
    public void Tumble_TouchesTheTableAtEachBounce()
    {
        Vector2[] path = [new(-100, 50), new(80, 60), new(140, 90), new(120, 80)];

        TumbleFrame[] frames = DiceTumble.Sample(path, 0, 360, 56, frames: 11);

        // Frames 4 and 7 fall exactly on the bounces (0.4 and 0.7): no lift above the shadow.
        Assert.AreEqual(frames[4].ShadowOffset.Y, frames[4].Offset.Y, Tolerance);
        Assert.AreEqual(frames[7].ShadowOffset.Y, frames[7].Offset.Y, Tolerance);
        Assert.IsTrue(frames[4].Scale.X > frames[4].Scale.Y, "Squashed flat on impact");
    }
}
