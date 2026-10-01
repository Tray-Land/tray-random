using System.Text.RegularExpressions;
using TrayRandom.Dice;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace TrayRandom.Tests;

[TestClass]
public sealed class DiceExpressionTests
{
    [TestMethod]
    [DataRow("d20", "d20")]
    [DataRow("1d20", "d20")]
    [DataRow(" 2D6 + 3 ", "2d6+3")]
    [DataRow("4d6k3", "4d6kh3")]
    [DataRow("2d20kl1", "2d20kl1")]
    [DataRow("3d8kh3", "3d8")]
    [DataRow("d%", "d100")]
    [DataRow("-1+d4", "-1+d4")]
    [DataRow("2d6-1d4+1", "2d6-d4+1")]
    public void Parse_Normalizes(string input, string expected) =>
        Assert.AreEqual(expected, DiceExpression.Parse(input).Normalized);

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("abc")]
    [DataRow("5")]
    [DataRow("2d")]
    [DataRow("d1")]
    [DataRow("0d6")]
    [DataRow("101d6")]
    [DataRow("d1001")]
    [DataRow("2d6+")]
    [DataRow("2d6 3")]
    [DataRow("4d6k5")]
    [DataRow("4d6k0")]
    [DataRow("d6*2")]
    public void TryParse_RejectsWithMessage(string input)
    {
        Assert.IsFalse(DiceExpression.TryParse(input, out DiceExpression? expression, out string? error));
        Assert.IsNull(expression);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void TryParse_RejectsTooManyTerms() =>
        Assert.IsFalse(DiceExpression.TryParse(string.Join("+", Enumerable.Repeat("d6", DiceExpression.MaxTerms + 1)), out _, out _));

    [TestMethod]
    public void Roll_StaysInRange()
    {
        DiceExpression expression = DiceExpression.Parse("3d6kh2-d4+5");
        (long min, long max) = expression.Range();
        Assert.AreEqual(2 - 4 + 5, min);
        Assert.AreEqual(12 - 1 + 5, max);

        Random random = new(1234);
        for (int i = 0; i < 2000; i++)
        {
            long total = expression.Roll(random).Total;
            Assert.IsTrue(total >= min && total <= max, $"{total} outside {min}..{max}");
        }
    }

    [TestMethod]
    public void Roll_HitsEveryFace()
    {
        DiceExpression expression = DiceExpression.Parse("d6");
        Random random = new(42);
        HashSet<long> seen = [];
        for (int i = 0; i < 500; i++)
        {
            seen.Add(expression.Roll(random).Total);
        }

        CollectionAssert.AreEquivalent(new long[] { 1, 2, 3, 4, 5, 6 }, seen.ToArray());
    }

    [TestMethod]
    public void Roll_KeepHighestDropsTheLowest()
    {
        DiceExpression expression = DiceExpression.Parse("4d6kh3");
        Random random = new(7);
        for (int i = 0; i < 200; i++)
        {
            DiceRoll roll = expression.Roll(random);
            IReadOnlyList<DieFace> dice = roll.Terms[0].Dice;
            Assert.AreEqual(3, dice.Count(d => d.Kept));
            Assert.IsTrue(dice.Single(d => !d.Kept).Value <= dice.Where(d => d.Kept).Min(d => d.Value));
            Assert.AreEqual(dice.Where(d => d.Kept).Sum(d => d.Value), roll.Total);
        }
    }

    [TestMethod]
    public void Breakdown_MarksDroppedDiceAndConstants()
    {
        DiceRoll roll = DiceExpression.Parse("2d20kl1-2").Roll(new Random(3));
        StringAssert.Matches(roll.Breakdown, new Regex(@"^\[(\d+, \(\d+\)|\(\d+\), \d+)\] - 2$"));
    }
}
