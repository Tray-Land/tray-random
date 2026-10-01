using System.Globalization;
using Bogus;
using TrayRandom.Dice;

namespace TrayRandom.Categories;

/// <summary>
/// Every category the app offers, grouped as the picker shows them. Ids are persisted (favorites,
/// last used), so they may be added or removed but never renamed.
/// </summary>
public static class CategoryCatalog
{
    // Segoe Fluent Icons code points.
    public static readonly CategoryGroup Chance = new("Dice & chance", "");
    public static readonly CategoryGroup People = new("People", "");
    public static readonly CategoryGroup Text = new("Text", "");
    public static readonly CategoryGroup Internet = new("Internet", "");
    public static readonly CategoryGroup Places = new("Places", "");
    public static readonly CategoryGroup Business = new("Business", "");
    public static readonly CategoryGroup Finance = new("Finance", "");
    public static readonly CategoryGroup Dates = new("Dates & times", "");
    public static readonly CategoryGroup Tech = new("Tech & IDs", "");
    public static readonly CategoryGroup Misc = new("Fun & misc", "");

    public static IReadOnlyList<string> DefaultFavorites { get; } = ["dice", "full-name", "email", "sentence", "guid"];

    public const string DefaultCategoryId = "dice";

    private static readonly string[] Suits = ["♠", "♥", "♦", "♣"];
    private static readonly string[] Ranks = ["A", "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K"];

    public static IReadOnlyList<RandomCategory> All { get; } =
    [
        // Dice & chance
        new("dice", "Dice roll", Chance, RollDice, CategoryInput.Dice),
        new("coin", "Coin flip", Chance, c => c.Random.Bool() ? "Heads" : "Tails"),
        new("number", "Number in range", Chance, NumberInRange, CategoryInput.Range),
        new("pick", "Pick from list", Chance, null, CategoryInput.List, PickFromList),
        new("shuffle", "Shuffle list", Chance, ShuffleList, CategoryInput.List),
        new("yes-no", "Yes or no", Chance, c => c.Random.Bool() ? "Yes" : "No"),
        new("card", "Playing card", Chance, c => c.Random.ArrayElement(Ranks) + c.Random.ArrayElement(Suits)),

        // People
        new("full-name", "Full name", People, c => c.Faker.Name.FullName()),
        new("first-name", "First name", People, c => c.Faker.Name.FirstName()),
        new("last-name", "Last name", People, c => c.Faker.Name.LastName()),
        new("username", "Username", People, c => c.Faker.Internet.UserName()),
        new("job-title", "Job title", People, c => c.Faker.Name.JobTitle()),
        new("phone", "Phone number", People, c => c.Faker.Phone.PhoneNumber()),
        new("person", "Person profile", People, PersonProfile),

        // Text
        new("word", "Word", Text, c => c.Faker.Lorem.Word()),
        new("words", "Words", Text, c => c.Faker.Lorem.Sentence(5).TrimEnd('.')),
        new("sentence", "Sentence", Text, c => c.Faker.Lorem.Sentence()),
        new("paragraph", "Paragraph", Text, c => c.Faker.Lorem.Paragraph()),
        new("slug", "Slug", Text, c => c.Faker.Lorem.Slug()),
        new("hacker-phrase", "Hacker phrase", Text, c => c.Faker.Hacker.Phrase()),
        new("catch-phrase", "Catch phrase", Text, c => c.Faker.Company.CatchPhrase()),
        new("buzzwords", "Business buzzwords", Text, c => c.Faker.Company.Bs()),
        new("review", "Product review", Text, c => c.Faker.Rant.Review()),

        // Internet
        new("email", "Email", Internet, c => c.Faker.Internet.Email()),
        new("url", "URL", Internet, c => c.Faker.Internet.UrlWithPath()),
        new("domain", "Domain name", Internet, c => c.Faker.Internet.DomainName()),
        new("ipv4", "IPv4 address", Internet, c => c.Faker.Internet.Ip()),
        new("ipv6", "IPv6 address", Internet, c => c.Faker.Internet.Ipv6()),
        new("mac", "MAC address", Internet, c => c.Faker.Internet.Mac()),
        new("port", "Port", Internet, c => c.Faker.Internet.Port().ToString(CultureInfo.InvariantCulture)),
        new("password", "Password", Internet, c => c.Faker.Internet.Password(16, memorable: false)),
        new("user-agent", "User agent", Internet, c => c.Faker.Internet.UserAgent()),
        new("color-hex", "Color (hex)", Internet, c => c.Faker.Internet.Color()),

        // Places
        new("address", "Full address", Places, c => c.Faker.Address.FullAddress()),
        new("street", "Street address", Places, c => c.Faker.Address.StreetAddress()),
        new("city", "City", Places, c => c.Faker.Address.City()),
        new("state", "State or region", Places, c => c.Faker.Address.State()),
        new("zip", "Postal code", Places, c => c.Faker.Address.ZipCode()),
        new("country", "Country", Places, c => c.Faker.Address.Country()),
        new("country-code", "Country code", Places, c => c.Faker.Address.CountryCode()),
        new("coordinates", "Coordinates", Places, Coordinates),
        new("time-zone", "Time zone", Places, c => c.Faker.Date.TimeZoneString()),

        // Business
        new("company", "Company name", Business, c => c.Faker.Company.CompanyName()),
        new("product", "Product name", Business, c => c.Faker.Commerce.ProductName()),
        new("department", "Department", Business, c => c.Faker.Commerce.Department(1)),
        new("price", "Price", Business, c => c.Faker.Commerce.Price()),
        new("ean13", "Barcode (EAN-13)", Business, c => c.Faker.Commerce.Ean13()),

        // Finance (fake: right shape and checksum where Bogus provides one, never real accounts)
        new("credit-card", "Credit card number", Finance, c => c.Faker.Finance.CreditCardNumber()),
        new("cvv", "Card CVV", Finance, c => c.Faker.Finance.CreditCardCvv()),
        new("iban", "IBAN", Finance, c => c.Faker.Finance.Iban(formatted: true)),
        new("bic", "BIC / SWIFT", Finance, c => c.Faker.Finance.Bic()),
        new("account", "Account number", Finance, c => c.Faker.Finance.Account()),
        new("routing", "Routing number", Finance, c => c.Faker.Finance.RoutingNumber()),
        new("amount", "Amount", Finance, c => c.Faker.Finance.Amount(1, 10_000).ToString("N2", CultureInfo.CurrentCulture)),
        new("currency", "Currency", Finance, Currency),
        new("bitcoin", "Bitcoin address", Finance, c => c.Faker.Finance.BitcoinAddress()),
        new("ethereum", "Ethereum address", Finance, c => c.Faker.Finance.EthereumAddress()),

        // Dates & times
        new("date-past", "Past date", Dates, c => c.Faker.Date.Past(5).ToString("d", CultureInfo.CurrentCulture)),
        new("date-future", "Future date", Dates, c => c.Faker.Date.Future(5).ToString("d", CultureInfo.CurrentCulture)),
        new("birthday", "Birthday", Dates, Birthday),
        new("time", "Time of day", Dates, c => c.Faker.Date.BetweenTimeOnly(TimeOnly.MinValue, TimeOnly.MaxValue).ToString("t", CultureInfo.CurrentCulture)),
        new("timestamp", "ISO timestamp", Dates, c => c.Faker.Date.RecentOffset(365).ToString("yyyy-MM-ddTHH:mm:ssK", CultureInfo.InvariantCulture)),
        new("weekday", "Weekday", Dates, c => c.Faker.Date.Weekday()),
        new("month", "Month", Dates, c => c.Faker.Date.Month()),

        // Tech & IDs
        new("guid", "GUID", Tech, c => c.Random.Guid().ToString()),
        new("hex", "Hex string", Tech, c => c.Random.Hexadecimal(16, prefix: string.Empty)),
        new("alphanumeric", "Random string", Tech, c => c.Random.AlphaNumeric(12)),
        new("hash", "SHA-1 hash", Tech, c => c.Random.Hash(40)),
        new("semver", "Version number", Tech, c => c.Faker.System.Semver()),
        new("file-name", "File name", Tech, c => c.Faker.System.FileName()),
        new("file-path", "File path", Tech, c => c.Faker.System.FilePath()),
        new("mime-type", "MIME type", Tech, c => c.Faker.System.MimeType()),
        new("db-column", "Database column", Tech, c => c.Faker.Database.Column()),

        // Fun & misc
        new("color-name", "Color name", Misc, c => c.Faker.Commerce.Color()),
        new("vehicle", "Vehicle", Misc, c => $"{c.Faker.Vehicle.Manufacturer()} {c.Faker.Vehicle.Model()}"),
        new("vin", "Vehicle VIN", Misc, c => c.Faker.Vehicle.Vin()),
        new("music-genre", "Music genre", Misc, c => c.Faker.Music.Genre()),
    ];

    public static IReadOnlyList<CategoryGroup> Groups { get; } = All.Select(c => c.Group).Distinct().ToArray();

    private static readonly Dictionary<string, RandomCategory> ById = All.ToDictionary(c => c.Id, StringComparer.Ordinal);

    public static RandomCategory? Find(string? id) => id is not null && ById.TryGetValue(id, out RandomCategory? c) ? c : null;

    public static bool Exists(string id) => ById.ContainsKey(id);

    private static GeneratedItem RollDice(GenerationContext c)
    {
        DiceExpression expression = DiceExpression.Parse(c.Inputs.DiceNotation);
        DiceRoll roll = expression.Roll(Random.Shared);

        // A lone die needs no working; anything else shows how the total came about.
        bool single = expression.Terms is [{ IsDice: true, Count: 1 }];
        return new GeneratedItem(
            roll.Total.ToString(CultureInfo.CurrentCulture),
            single ? expression.Normalized : $"{expression.Normalized}: {roll.Breakdown}");
    }

    private static GeneratedItem NumberInRange(GenerationContext c)
    {
        (long min, long max) = (Math.Min(c.Inputs.RangeMin, c.Inputs.RangeMax), Math.Max(c.Inputs.RangeMin, c.Inputs.RangeMax));
        return c.Random.Long(min, max).ToString(CultureInfo.CurrentCulture);
    }

    private static IReadOnlyList<GeneratedItem> PickFromList(GenerationContext c, int count)
    {
        IReadOnlyList<string> items = RequireItems(c);

        // Without replacement while the list lasts, like drawing names from a hat.
        List<GeneratedItem> picks = [];
        while (picks.Count < count)
        {
            picks.AddRange(c.Random.Shuffle(items).Take(count - picks.Count).Select(s => new GeneratedItem(s)));
        }

        return picks;
    }

    private static GeneratedItem ShuffleList(GenerationContext c) =>
        string.Join(", ", c.Random.Shuffle(RequireItems(c)));

    private static IReadOnlyList<string> RequireItems(GenerationContext c)
    {
        IReadOnlyList<string> items = ListItems.Parse(c.Inputs.ListText);
        return items.Count > 0 ? items : throw new ArgumentException("Add some items to the list first, one per line or separated by commas.");
    }

    private static GeneratedItem PersonProfile(GenerationContext c)
    {
        Person p = new(c.Faker.Locale);
        string profile = string.Join(Environment.NewLine,
            p.FullName,
            p.Email,
            p.Phone,
            $"{p.Address.Street}, {p.Address.City} {p.Address.ZipCode}",
            p.DateOfBirth.ToString("d", CultureInfo.CurrentCulture));
        return new GeneratedItem(profile);
    }

    private static GeneratedItem Coordinates(GenerationContext c) =>
        string.Create(CultureInfo.InvariantCulture, $"{c.Faker.Address.Latitude():F6}, {c.Faker.Address.Longitude():F6}");

    private static GeneratedItem Currency(GenerationContext c)
    {
        Bogus.DataSets.Currency currency = c.Faker.Finance.Currency();
        return new GeneratedItem(currency.Code, currency.Description);
    }

    private static GeneratedItem Birthday(GenerationContext c)
    {
        DateTime today = DateTime.Today;
        DateTime birthday = c.Faker.Date.Between(today.AddYears(-90), today.AddYears(-18));
        int age = today.Year - birthday.Year - (birthday.Date > today.AddYears(-(today.Year - birthday.Year)) ? 1 : 0);
        return new GeneratedItem(birthday.ToString("d", CultureInfo.CurrentCulture), $"Age {age}");
    }
}
