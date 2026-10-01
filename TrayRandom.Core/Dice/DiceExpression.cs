using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TrayRandom.Dice;

/// <summary>
/// Standard tabletop dice notation: terms joined by + or -, where a term is a constant
/// (<c>3</c>) or dice (<c>2d6</c>, <c>d20</c>, <c>d%</c> for d100), optionally keeping the
/// highest or lowest few (<c>4d6kh3</c>, <c>2d20kl1</c>; <c>k3</c> means <c>kh3</c>).
/// Whitespace and case are ignored.
/// </summary>
public sealed partial class DiceExpression
{
    public const int MaxDicePerTerm = 100;
    public const int MaxSides = 1000;
    public const int MaxTerms = 20;
    public const int MaxConstant = 100_000;

    private DiceExpression(IReadOnlyList<DiceTerm> terms, string normalized)
    {
        Terms = terms;
        Normalized = normalized;
    }

    public IReadOnlyList<DiceTerm> Terms { get; }

    /// <summary>The canonical spelling, e.g. "4d6kh3+2" for " 4D6K3 + 2 ".</summary>
    public string Normalized { get; }

    public override string ToString() => Normalized;

    public static DiceExpression Parse(string text) =>
        TryParse(text, out DiceExpression? expression, out string? error)
            ? expression
            : throw new FormatException(error);

    public static bool TryParse(string? text, [NotNullWhen(true)] out DiceExpression? expression, [NotNullWhen(false)] out string? error)
    {
        expression = null;

        // Spaces may sit around operators, but "2d6 3" is a typo, not 2d63.
        if (text is not null && GapBetweenTokens().IsMatch(text))
        {
            error = "Put + or - between terms.";
            return false;
        }

        string compact =string.Concat((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
        if (compact.Length == 0)
        {
            error = "Enter dice like 2d6+3.";
            return false;
        }

        List<DiceTerm> terms = [];
        int pos = 0;
        while (pos < compact.Length)
        {
            int sign = 1;
            if (compact[pos] is '+' or '-')
            {
                sign = compact[pos] == '-' ? -1 : 1;
                pos++;
            }
            else if (terms.Count > 0)
            {
                error = $"Expected + or - before \"{compact[pos..]}\".";
                return false;
            }

            if (!TryParseTerm(compact, ref pos, sign, out DiceTerm? term, out error))
            {
                return false;
            }

            terms.Add(term);
            if (terms.Count > MaxTerms)
            {
                error = $"Use at most {MaxTerms} terms.";
                return false;
            }
        }

        if (!terms.Any(t => t.IsDice))
        {
            error = "Include at least one die, like d20.";
            return false;
        }

        expression = new DiceExpression(terms, Normalize(terms));
        error = null;
        return true;
    }

    public DiceRoll Roll(Random random)
    {
        List<DiceTermRoll> rolls = new(Terms.Count);
        long total = 0;
        foreach (DiceTerm term in Terms)
        {
            DiceTermRoll roll = term.Roll(random);
            rolls.Add(roll);
            total += roll.Value;
        }

        return new DiceRoll(this, rolls, total);
    }

    /// <summary>Smallest and largest possible totals, for showing the range next to a roll.</summary>
    public (long Min, long Max) Range()
    {
        long min = 0, max = 0;
        foreach (DiceTerm t in Terms)
        {
            long lo = t.IsDice ? t.KeptCount : t.Constant;
            long hi = t.IsDice ? (long)t.KeptCount * t.Sides : t.Constant;
            if (t.Sign > 0)
            {
                min += lo;
                max += hi;
            }
            else
            {
                min -= hi;
                max -= lo;
            }
        }

        return (min, max);
    }

    private static bool TryParseTerm(string s, ref int pos, int sign, [NotNullWhen(true)] out DiceTerm? term, [NotNullWhen(false)] out string? error)
    {
        term = null;
        int? count = ReadNumber(s, ref pos);
        if (pos >= s.Length || s[pos] != 'd')
        {
            if (count is null)
            {
                error = pos >= s.Length ? "The expression ends with an operator." : $"Unexpected \"{s[pos]}\".";
                return false;
            }

            if (count > MaxConstant)
            {
                error = $"Keep numbers under {MaxConstant:N0}.";
                return false;
            }

            term = DiceTerm.ConstantTerm(sign, count.Value);
            error = null;
            return true;
        }

        pos++; // 'd'
        int dice = count ?? 1;
        int? sides;
        if (pos < s.Length && s[pos] == '%')
        {
            pos++;
            sides = 100;
        }
        else
        {
            sides = ReadNumber(s, ref pos);
        }

        if (sides is null)
        {
            error = "Say how many sides, like d6.";
            return false;
        }

        if (dice < 1 || dice > MaxDicePerTerm)
        {
            error = $"Roll between 1 and {MaxDicePerTerm} dice at a time.";
            return false;
        }

        if (sides < 2 || sides > MaxSides)
        {
            error = $"Dice need between 2 and {MaxSides} sides.";
            return false;
        }

        KeepMode keep = KeepMode.All;
        int keepCount = dice;
        if (pos < s.Length && s[pos] == 'k')
        {
            pos++;
            keep = KeepMode.Highest;
            if (pos < s.Length && s[pos] is 'h' or 'l')
            {
                keep = s[pos] == 'l' ? KeepMode.Lowest : KeepMode.Highest;
                pos++;
            }

            int? n = ReadNumber(s, ref pos);
            if (n is null || n < 1 || n > dice)
            {
                error = $"Keep between 1 and {dice} dice.";
                return false;
            }

            keepCount = n.Value;
            if (keepCount == dice)
            {
                keep = KeepMode.All;
            }
        }

        term = new DiceTerm(sign, dice, sides.Value, keep, keepCount, 0);
        error = null;
        return true;
    }

    private static int? ReadNumber(string s, ref int pos)
    {
        int start = pos;
        while (pos < s.Length && char.IsAsciiDigit(s[pos]) && pos - start < 7)
        {
            pos++;
        }

        return pos == start ? null : int.Parse(s.AsSpan(start, pos - start), CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"[0-9a-z%]\s+[0-9a-z%]", RegexOptions.IgnoreCase)]
    private static partial Regex GapBetweenTokens();

    private static string Normalize(IReadOnlyList<DiceTerm> terms)
    {
        StringBuilder sb = new();
        for (int i = 0; i < terms.Count; i++)
        {
            DiceTerm t = terms[i];
            if (i > 0 || t.Sign < 0)
            {
                sb.Append(t.Sign < 0 ? '-' : '+');
            }

            sb.Append(t.ToString());
        }

        return sb.ToString();
    }
}

public enum KeepMode
{
    All,
    Highest,
    Lowest,
}

public sealed record DiceTerm(int Sign, int Count, int Sides, KeepMode Keep, int KeptCount, int Constant)
{
    public bool IsDice => Sides > 0;

    public static DiceTerm ConstantTerm(int sign, int value) => new(sign, 0, 0, KeepMode.All, 0, value);

    public DiceTermRoll Roll(Random random)
    {
        if (!IsDice)
        {
            return new DiceTermRoll(this, [], Sign * Constant);
        }

        int[] faces = new int[Count];
        for (int i = 0; i < Count; i++)
        {
            faces[i] = random.Next(1, Sides + 1);
        }

        // Mark which dice count: the highest or lowest few, ties broken left to right.
        bool[] kept = new bool[Count];
        IEnumerable<int> order = Keep switch
        {
            KeepMode.Highest => Enumerable.Range(0, Count).OrderByDescending(i => faces[i]).ThenBy(i => i),
            KeepMode.Lowest => Enumerable.Range(0, Count).OrderBy(i => faces[i]).ThenBy(i => i),
            _ => Enumerable.Range(0, Count),
        };
        foreach (int i in order.Take(KeptCount))
        {
            kept[i] = true;
        }

        long sum = 0;
        DieFace[] dice = new DieFace[Count];
        for (int i = 0; i < Count; i++)
        {
            dice[i] = new DieFace(faces[i], kept[i]);
            if (kept[i])
            {
                sum += faces[i];
            }
        }

        return new DiceTermRoll(this, dice, Sign * sum);
    }

    public override string ToString()
    {
        if (!IsDice)
        {
            return Constant.ToString(CultureInfo.InvariantCulture);
        }

        string dice = Count == 1 ? $"d{Sides}" : $"{Count}d{Sides}";
        return Keep switch
        {
            KeepMode.Highest => $"{dice}kh{KeptCount}",
            KeepMode.Lowest => $"{dice}kl{KeptCount}",
            _ => dice,
        };
    }
}

public readonly record struct DieFace(int Value, bool Kept);

public sealed record DiceTermRoll(DiceTerm Term, IReadOnlyList<DieFace> Dice, long Value);

public sealed record DiceRoll(DiceExpression Expression, IReadOnlyList<DiceTermRoll> Terms, long Total)
{
    /// <summary>
    /// The working, e.g. "[6, 5, 3, (1)] + 2": each term's faces in brackets, with dropped dice in
    /// parentheses, or the constant on its own.
    /// </summary>
    public string Breakdown
    {
        get
        {
            StringBuilder sb = new();
            for (int i = 0; i < Terms.Count; i++)
            {
                DiceTermRoll t = Terms[i];
                if (i > 0)
                {
                    sb.Append(t.Term.Sign < 0 ? " - " : " + ");
                }
                else if (t.Term.Sign < 0)
                {
                    sb.Append('-');
                }

                if (!t.Term.IsDice)
                {
                    sb.Append(t.Term.Constant.ToString(CultureInfo.InvariantCulture));
                    continue;
                }

                sb.Append('[');
                sb.AppendJoin(", ", t.Dice.Select(d => d.Kept
                    ? d.Value.ToString(CultureInfo.InvariantCulture)
                    : $"({d.Value.ToString(CultureInfo.InvariantCulture)})"));
                sb.Append(']');
            }

            return sb.ToString();
        }
    }
}
