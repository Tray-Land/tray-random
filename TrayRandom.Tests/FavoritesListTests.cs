using TrayRandom.Favorites;

namespace TrayRandom.Tests;

[TestClass]
public sealed class FavoritesListTests
{
    private static readonly HashSet<string> Known = ["a", "b", "c", "d"];

    [TestMethod]
    public void Sanitize_DropsUnknownAndDuplicates() =>
        CollectionAssert.AreEqual(new[] { "b", "a", "d" }, FavoritesList.Sanitize(["b", "zzz", "a", "b", "", "d"], Known.Contains).ToArray());

    [TestMethod]
    public void Sanitize_HandlesNull() => Assert.AreEqual(0, FavoritesList.Sanitize(null, Known.Contains).Count);

    [TestMethod]
    public void Toggle_AddsAtEndThenRemoves()
    {
        IReadOnlyList<string> added = FavoritesList.Toggle(["a", "b"], "c");
        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, added.ToArray());
        CollectionAssert.AreEqual(new[] { "a", "c" }, FavoritesList.Toggle(added, "b").ToArray());
    }

    [TestMethod]
    public void Toggle_StopsAtMax()
    {
        string[] full = Enumerable.Range(0, FavoritesList.MaxFavorites).Select(i => i.ToString()).ToArray();
        Assert.AreEqual(FavoritesList.MaxFavorites, FavoritesList.Toggle(full, "new").Count);
    }

    [TestMethod]
    [DataRow("a", -1, "a,b,c")]
    [DataRow("b", -1, "b,a,c")]
    [DataRow("b", 1, "a,c,b")]
    [DataRow("c", 1, "a,b,c")]
    [DataRow("x", 1, "a,b,c")]
    public void Move_ShiftsWithinBounds(string id, int offset, string expected) =>
        Assert.AreEqual(expected, string.Join(",", FavoritesList.Move(["a", "b", "c"], id, offset)));

    [TestMethod]
    [DataRow("a", 1, "b")]
    [DataRow("c", 1, "a")]
    [DataRow("a", -1, "c")]
    [DataRow("not-a-favorite", 1, "a")]
    [DataRow("not-a-favorite", -1, "c")]
    public void Cycle_Wraps(string current, int direction, string expected) =>
        Assert.AreEqual(expected, FavoritesList.Cycle(["a", "b", "c"], current, direction));

    [TestMethod]
    public void Cycle_EmptyIsNull() => Assert.IsNull(FavoritesList.Cycle([], "a", 1));
}
