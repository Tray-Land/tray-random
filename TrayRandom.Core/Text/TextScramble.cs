namespace TrayRandom.Text;

/// <summary>
/// Frames for a "decoding" text effect: letters and digits resolve left to right, and the rest of
/// the text shows random characters of the same kind. Spaces, line breaks, and punctuation never
/// change, so the text keeps its shape while it settles.
/// </summary>
public static class TextScramble
{
    private const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private const string Lower = "abcdefghijklmnopqrstuvwxyz";
    private const string Digits = "0123456789";

    /// <summary>The text at <paramref name="progress"/> (0 = fully scrambled, 1 = the real text).</summary>
    public static string Frame(string target, double progress, Random random)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(random);

        if (progress >= 1 || target.Length == 0)
        {
            return target;
        }

        return string.Create(target.Length, (target, progress, random), static (span, state) =>
        {
            (string text, double done, Random rng) = state;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool settled = (double)i / text.Length < done;
                span[i] = settled ? c : Scramble(c, rng);
            }
        });
    }

    private static char Scramble(char c, Random random)
    {
        if (char.IsAsciiDigit(c))
        {
            return Digits[random.Next(Digits.Length)];
        }

        if (char.IsAsciiLetterUpper(c))
        {
            return Upper[random.Next(Upper.Length)];
        }

        if (char.IsAsciiLetterLower(c))
        {
            return Lower[random.Next(Lower.Length)];
        }

        // Accented and non-Latin letters: any lowercase stand-in keeps the rhythm without
        // inventing glyphs the font may lack. Punctuation and whitespace stay put.
        return char.IsLetterOrDigit(c) ? Lower[random.Next(Lower.Length)] : c;
    }
}
