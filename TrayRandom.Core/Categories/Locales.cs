namespace TrayRandom.Categories;

/// <summary>The Bogus locales offered in Settings, with readable names.</summary>
public static class Locales
{
    public const string Default = "en";

    public static IReadOnlyList<(string Code, string Name)> All { get; } =
    [
        ("af_ZA", "Afrikaans (South Africa)"),
        ("ar", "Arabic"),
        ("az", "Azerbaijani"),
        ("cz", "Czech"),
        ("de", "German"),
        ("de_AT", "German (Austria)"),
        ("de_CH", "German (Switzerland)"),
        ("el", "Greek"),
        ("en", "English"),
        ("en_AU", "English (Australia)"),
        ("en_AU_ocker", "English (Australia Ocker)"),
        ("en_BORK", "English (Bork)"),
        ("en_CA", "English (Canada)"),
        ("en_GB", "English (Great Britain)"),
        ("en_IE", "English (Ireland)"),
        ("en_IND", "English (India)"),
        ("en_NG", "English (Nigeria)"),
        ("en_US", "English (United States)"),
        ("en_ZA", "English (South Africa)"),
        ("es", "Spanish"),
        ("es_MX", "Spanish (Mexico)"),
        ("fa", "Farsi"),
        ("fi", "Finnish"),
        ("fr", "French"),
        ("fr_CA", "French (Canada)"),
        ("fr_CH", "French (Switzerland)"),
        ("ge", "Georgian"),
        ("hr", "Croatian"),
        ("id_ID", "Indonesian"),
        ("it", "Italian"),
        ("ja", "Japanese"),
        ("ko", "Korean"),
        ("lv", "Latvian"),
        ("nb_NO", "Norwegian"),
        ("ne", "Nepalese"),
        ("nl", "Dutch"),
        ("nl_BE", "Dutch (Belgium)"),
        ("pl", "Polish"),
        ("pt_BR", "Portuguese (Brazil)"),
        ("pt_PT", "Portuguese (Portugal)"),
        ("ro", "Romanian"),
        ("ru", "Russian"),
        ("sk", "Slovak"),
        ("sv", "Swedish"),
        ("tr", "Turkish"),
        ("uk", "Ukrainian"),
        ("vi", "Vietnamese"),
        ("zh_CN", "Chinese"),
        ("zh_TW", "Chinese (Taiwan)"),
        ("zu_ZA", "Zulu (South Africa)"),
    ];

    /// <summary>The locale if Bogus has it, otherwise <see cref="Default"/>.</summary>
    public static string Resolve(string? code) =>
        code is not null && All.Any(l => l.Code == code) ? code : Default;
}
