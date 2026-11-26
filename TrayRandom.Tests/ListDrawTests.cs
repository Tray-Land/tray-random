using TrayRandom.Draws;

namespace TrayRandom.Tests;

[TestClass]
public sealed class ListDrawTests
{
    [TestMethod]
    public void Run_Winner_ReturnsOneOriginalEntry()
    {
        DrawLine[] result = ListDraw.Run("Option A\nOption B\nOption C", DrawMode.Winner, random: new Random(1));

        Assert.HasCount(1, result);
        CollectionAssert.Contains(new[] { "Option A", "Option B", "Option C" }, result[0].Entry);
    }

    [TestMethod]
    public void Run_Ranking_ShufflesAllEntriesAndKeepsDuplicateRowsAndCommas()
    {
        const string entries = "  Seattle, WA  \r\n\r\nChicago | 12\nNew York\rChicago";

        DrawLine[] result = ListDraw.Run(entries, DrawMode.Ranking, random: new Random(5));

        string[] original = ["Seattle, WA", "Chicago", "New York", "Chicago"];
        string[] ranked = result.Select(line => line.Entry).ToArray();
        CollectionAssert.AreEquivalent(original, ranked);
        CollectionAssert.AreNotEqual(original, ranked);
    }

    [TestMethod]
    [DataRow(DrawMode.WeightedWinner, "A")]
    [DataRow(DrawMode.WeightedRanking, "A,B,D")]
    public void Run_WeightedDraw_FavorsHigherWeightsAndExcludesZeroWeight(DrawMode mode, string expected)
    {
        const string entries = "A | 20\nB\nC | 0\nD | 2";

        DrawLine[] result = ListDraw.Run(entries, mode, random: new DrawRandom(0.8, 0.2, 0.4));

        Assert.AreEqual(expected, string.Join(",", result.Select(line => line.Entry)));
    }

    [TestMethod]
    [DataRow(3, "3,2,2")]
    [DataRow(7, "1,1,1,1,1,1,1")]
    public void Run_Groups_AssignsEveryoneOnceAndBalancesGroupSizes(int groupCount, string sizes)
    {
        string[] entries = ["A", "B", "C", "D", "E", "F", "G"];

        DrawLine[] result = ListDraw.Run(string.Join('\n', entries), DrawMode.Groups, groupCount, new Random(1));

        CollectionAssert.AreEquivalent(entries, result.Select(line => line.Entry).ToArray());
        Assert.AreEqual(sizes, string.Join(",", result.GroupBy(line => line.Group).Select(group => group.Count())));
    }

    [TestMethod]
    [DataRow("A\nB")]
    [DataRow("A\nB\nC\nD\nE")]
    public void Run_SecretSanta_AssignsEveryoneOneOtherRecipient(string entries)
    {
        DrawLine[] result = ListDraw.Run(entries, DrawMode.SecretSanta, random: new Random(1));

        string[] names = entries.Split('\n');
        CollectionAssert.AreEquivalent(names, result.Select(line => line.Entry).ToArray());
        CollectionAssert.AreEquivalent(names, result.Select(line => line.Recipient).ToArray());
        Assert.IsTrue(result.All(line => line.Entry != line.Recipient));
    }

    [TestMethod]
    [DataRow("\n \r\n", DrawMode.Ranking, 0, DrawError.EmptyList, 0)]
    [DataRow("A\n\nB | -1", DrawMode.WeightedWinner, 0, DrawError.InvalidWeight, 3)]
    [DataRow("A | 2147483648", DrawMode.WeightedRanking, 0, DrawError.InvalidWeight, 1)]
    [DataRow("A | 0\nB | 0", DrawMode.WeightedWinner, 0, DrawError.NoPositiveWeights, 0)]
    [DataRow("A\nB", DrawMode.Groups, 3, DrawError.InvalidGroupCount, 0)]
    [DataRow("A\nB", DrawMode.Groups, 1, DrawError.InvalidGroupCount, 0)]
    [DataRow("A", DrawMode.SecretSanta, 0, DrawError.NotEnoughForSecretSanta, 0)]
    [DataRow("A\n a ", DrawMode.SecretSanta, 0, DrawError.DuplicateSecretSantaEntry, 0)]
    public void Run_InvalidInput_ReportsReasonAndLine(
        string entries, DrawMode mode, int groupCount, DrawError expected, int line)
    {
        DrawValidationException error = Assert.ThrowsExactly<DrawValidationException>(() => ListDraw.Run(entries, mode, groupCount));

        Assert.AreEqual(expected, error.Error);
        Assert.AreEqual(line, error.LineNumber);
    }

    private sealed class DrawRandom(params double[] values) : Random
    {
        private int _index;

        public override double NextDouble() => values[_index++];
    }
}
