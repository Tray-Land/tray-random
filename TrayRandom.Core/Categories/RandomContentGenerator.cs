using Bogus;

namespace TrayRandom.Categories;

/// <summary>Makes batches of results for a category in one Bogus locale.</summary>
public sealed class RandomContentGenerator
{
    private readonly Faker _faker;
    private Faker? _english;

    public RandomContentGenerator(string? locale = null)
    {
        Locale = Locales.Resolve(locale);
        _faker = new Faker(Locale);
    }

    public string Locale { get; }

    /// <summary>
    /// Generates <paramref name="count"/> results (at least 1; there is no upper limit).
    /// Throws <see cref="ArgumentException"/> or <see cref="FormatException"/> with a message fit
    /// to show the user when the category's input is unusable.
    /// </summary>
    public IReadOnlyList<GeneratedItem> Generate(RandomCategory category, CategoryInputs inputs, int count = 1)
    {
        count = Math.Max(count, 1);
        GenerationContext context = new(_faker, inputs);
        if (category.GenerateBatch is { } batch)
        {
            return batch(context, count);
        }

        GeneratedItem[] items = new GeneratedItem[count];
        for (int i = 0; i < count; i++)
        {
            items[i] = category.Generate!(context);

            // Some locales lack a dataset (Czech has no states) and Bogus returns "" for it.
            if (string.IsNullOrWhiteSpace(items[i].Value) && Locale != Locales.Default)
            {
                _english ??= new Faker(Locales.Default);
                items[i] = category.Generate(new GenerationContext(_english, inputs));
            }
        }

        return items;
    }
}
