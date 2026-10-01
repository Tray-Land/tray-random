using System.Globalization;
using System.Text.RegularExpressions;
using TrayRandom.Categories;

namespace TrayRandom.Tests;

[TestClass]
public sealed class CategoryCatalogTests
{
    private static readonly CategoryInputs Inputs = new()
    {
        DiceNotation = "3d6+1",
        RangeMin = 10,
        RangeMax = 20,
        ListText = "Alice\nBob\nCarol",
    };

    public static IEnumerable<object[]> LocaleCodes => Locales.All.Select(l => new object[] { l.Code });

    [TestMethod]
    public void Ids_AreUniqueAndKebabCase()
    {
        Assert.AreEqual(CategoryCatalog.All.Count, CategoryCatalog.All.Select(c => c.Id).Distinct().Count());
        foreach (RandomCategory c in CategoryCatalog.All)
        {
            StringAssert.Matches(c.Id, new Regex("^[a-z0-9]+(-[a-z0-9]+)*$"));
            Assert.IsTrue(c.Generate is not null || c.GenerateBatch is not null, c.Id);
        }
    }

    [TestMethod]
    public void Defaults_Exist()
    {
        Assert.IsNotNull(CategoryCatalog.Find(CategoryCatalog.DefaultCategoryId));
        foreach (string id in CategoryCatalog.DefaultFavorites)
        {
            Assert.IsTrue(CategoryCatalog.Exists(id), id);
        }
    }

    // Bogus fills locale gaps from English, but a broken dataset or API misuse would throw here.
    [TestMethod]
    [DynamicData(nameof(LocaleCodes))]
    public void EveryCategory_GeneratesInEveryLocale(string locale)
    {
        RandomContentGenerator generator = new(locale);
        Assert.AreEqual(locale, generator.Locale);
        foreach (RandomCategory category in CategoryCatalog.All)
        {
            IReadOnlyList<GeneratedItem> items = generator.Generate(category, Inputs, 3);
            Assert.AreEqual(3, items.Count, category.Id);
            Assert.IsTrue(items.All(i => !string.IsNullOrWhiteSpace(i.Value)), $"{locale}/{category.Id} produced an empty value");
        }
    }

    [TestMethod]
    public void Count_IsClamped()
    {
        RandomContentGenerator generator = new();
        RandomCategory guid = CategoryCatalog.Find("guid")!;
        Assert.AreEqual(1, generator.Generate(guid, Inputs, 0).Count);
        Assert.AreEqual(RandomContentGenerator.MaxCount, generator.Generate(guid, Inputs, 999).Count);
    }

    [TestMethod]
    public void UnknownLocale_FallsBackToEnglish() => Assert.AreEqual("en", new RandomContentGenerator("xx_YY").Locale);

    [TestMethod]
    public void Pick_DoesNotRepeatUntilTheListRunsOut()
    {
        RandomContentGenerator generator = new();
        RandomCategory pick = CategoryCatalog.Find("pick")!;
        IReadOnlyList<GeneratedItem> three = generator.Generate(pick, Inputs, 3);
        CollectionAssert.AreEquivalent(new[] { "Alice", "Bob", "Carol" }, three.Select(i => i.Value).ToArray());
        Assert.AreEqual(5, generator.Generate(pick, Inputs, 5).Count);
    }

    [TestMethod]
    public void Number_StaysInRangeEvenWhenReversed()
    {
        RandomContentGenerator generator = new();
        RandomCategory number = CategoryCatalog.Find("number")!;
        foreach (GeneratedItem item in generator.Generate(number, Inputs with { RangeMin = 20, RangeMax = 10 }, 50))
        {
            long value = long.Parse(item.Value, NumberStyles.Any, CultureInfo.CurrentCulture);
            Assert.IsTrue(value is >= 10 and <= 20, item.Value);
        }
    }

    [TestMethod]
    public void Dice_ShowsWorking()
    {
        GeneratedItem item = new RandomContentGenerator().Generate(CategoryCatalog.Find("dice")!, Inputs)[0];
        Assert.IsNotNull(item.Detail);
        StringAssert.StartsWith(item.Detail, "3d6+1: [");
    }

    [TestMethod]
    public void BadInput_ThrowsUserFacingMessage()
    {
        RandomContentGenerator generator = new();
        Assert.ThrowsExactly<FormatException>(() => generator.Generate(CategoryCatalog.Find("dice")!, Inputs with { DiceNotation = "lots" }));
        Assert.ThrowsExactly<ArgumentException>(() => generator.Generate(CategoryCatalog.Find("pick")!, Inputs with { ListText = " , " }));
    }

    [TestMethod]
    [DataRow("a, b ,c", "a|b|c")]
    [DataRow("one, two\nthree", "one, two|three")]
    [DataRow("x\r\n\r\ny\n", "x|y")]
    [DataRow("   ", "")]
    public void ListItems_SplitsByLineOrComma(string text, string expected) =>
        Assert.AreEqual(expected, string.Join("|", ListItems.Parse(text)));
}
