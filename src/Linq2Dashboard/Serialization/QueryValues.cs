using System.Globalization;
using System.Text;

namespace Linq2Dashboard.Serialization;

/// <summary>
/// Shared helpers for the query-string form of selections (design §2.5): the token list with its
/// escaping, the number and instant text forms, and the minimal percent-encoding that keeps a URL readable.
/// </summary>
internal static class QueryValues
{
    /// <summary>The token that means the null facet value, or "include the null rows", in every facet kind.</summary>
    public const string NullToken = "null";

    /// <summary>The token that means the empty string in a value list, since an empty token is dropped.</summary>
    public const string EmptyToken = "\"\"";

    /// <summary>The separator between the bounds of an interval.</summary>
    public const string IntervalSeparator = "..";

    /// <summary>
    /// Writes one value of a comma-separated list. A backslash escapes the next character, so a literal
    /// comma or backslash is written escaped. The empty string is written <c>""</c>, since an empty token is
    /// dropped; the literal texts "null" and <c>""</c> are written with a leading backslash so they read as text.
    /// </summary>
    public static string EscapeToken(string value)
    {
        if (value.Length == 0)
        {
            return EmptyToken;
        }

        if (value == NullToken || value == EmptyToken)
        {
            return "\\" + value;
        }

        if (value.IndexOfAny([',', '\\']) < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 4);
        foreach (char c in value)
        {
            if (c is ',' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits a comma-separated list into its values. An unescaped token that is exactly "null" is the null
    /// value (returned as <c>null</c>) and <c>""</c> is the empty string; an empty token is dropped, so
    /// <c>SE,,NO</c> reads leniently.
    /// </summary>
    public static List<string?> SplitTokens(string text)
    {
        var tokens = new List<string?>();
        var current = new StringBuilder();
        bool escaped = false;
        bool anyEscape = false;

        void Flush()
        {
            if (anyEscape)
            {
                tokens.Add(current.ToString());
            }
            else if (current.Length > 0)
            {
                string token = current.ToString();
                tokens.Add(token == NullToken ? null : token == EmptyToken ? string.Empty : token);
            }

            current.Clear();
            anyEscape = false;
        }

        foreach (char c in text)
        {
            if (escaped)
            {
                current.Append(c);
                escaped = false;
            }
            else if (c == '\\')
            {
                escaped = true;
                anyEscape = true;
            }
            else if (c == ',')
            {
                Flush();
            }
            else
            {
                current.Append(c);
            }
        }

        Flush();
        return tokens;
    }

    /// <summary>
    /// Splits <c>first,rest...</c> into the first token and whether every remaining token is the null token.
    /// Returns false when a remaining token is anything else, so <c>100..500,SE</c> does not read.
    /// </summary>
    public static bool SplitNullSuffix(string text, out string first, out bool includeNull)
    {
        includeNull = false;
        int comma = text.IndexOf(',');
        first = comma < 0 ? text : text[..comma];
        if (comma < 0)
        {
            return true;
        }

        foreach (string part in text[(comma + 1)..].Split(','))
        {
            if (part != NullToken)
            {
                return false;
            }

            includeNull = true;
        }

        return true;
    }

    /// <summary>
    /// Writes an interval: <c>from..to</c> when both ends are inclusive (the default), otherwise in bracket
    /// notation with <c>[</c> or <c>]</c> for an inclusive end and <c>(</c> or <c>)</c> for an exclusive one, as
    /// in <c>[100..500)</c>. An empty bound is unbounded.
    /// </summary>
    public static string FormatInterval(string from, string to, bool fromInclusive = true, bool toInclusive = true)
    {
        string body = from + IntervalSeparator + to;
        return fromInclusive && toInclusive
            ? body
            : (fromInclusive ? "[" : "(") + body + (toInclusive ? "]" : ")");
    }

    /// <summary>Removes the optional brackets around an interval and reports what they said; no brackets mean inclusive.</summary>
    public static string StripBrackets(string text, out bool fromInclusive, out bool toInclusive)
    {
        fromInclusive = true;
        toInclusive = true;
        if (text.Length > 0 && text[0] is '[' or '(')
        {
            fromInclusive = text[0] == '[';
            text = text[1..];
        }

        if (text.Length > 0 && text[^1] is ']' or ')')
        {
            toInclusive = text[^1] == ']';
            text = text[..^1];
        }

        return text;
    }

    public static string FormatDouble(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Reads a number. As in <see cref="TryParseInstant"/>, a space where an exponent's plus was is read as the plus.</summary>
    public static bool TryParseDouble(string text, out double value)
    {
        if (text.Contains(' '))
        {
            text = text.Replace(' ', '+');
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>
    /// The shortest ISO 8601 text that round-trips the instant: seconds and fraction only when they are not
    /// zero, <c>Z</c> for a zero offset. <c>2026-03-01T00:00+01:00</c> rather than the JSON form's seven decimals.
    /// </summary>
    public static string FormatInstant(DateTimeOffset value)
    {
        var builder = new StringBuilder(32);
        builder.Append(value.ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture));
        long fraction = value.Ticks % TimeSpan.TicksPerSecond;
        if (value.Second != 0 || fraction != 0)
        {
            builder.Append(value.ToString(":ss", CultureInfo.InvariantCulture));
            if (fraction != 0)
            {
                builder.Append('.').Append(fraction.ToString("0000000", CultureInfo.InvariantCulture).TrimEnd('0'));
            }
        }

        builder.Append(value.Offset == TimeSpan.Zero ? "Z" : value.ToString("zzz", CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    /// <summary>
    /// Reads an ISO 8601 instant. A <c>+</c> in a hand-written URL decodes as a space, so a text with a space
    /// where the offset sign belongs is retried with the plus restored.
    /// </summary>
    public static bool TryParseInstant(string text, out DateTimeOffset value)
    {
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
        {
            return true;
        }

        return text.Contains(' ') && DateTimeOffset.TryParse(text.Replace(' ', '+'), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    /// <summary>
    /// Percent-encodes a query name or value, leaving everything a browser shows as-is: letters, digits,
    /// <c>-._~</c> and the punctuation that is safe inside a query component, including <c>, : / [ ] ( )</c>.
    /// <c>&amp; = + # %</c>, space, quotes and non-ASCII are encoded.
    /// </summary>
    public static string Encode(string text)
    {
        StringBuilder? builder = null;
        int runStart = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (IsSafe(text[i]))
            {
                continue;
            }

            builder ??= new StringBuilder(text.Length + 8);
            builder.Append(text, runStart, i - runStart);
            // A surrogate pair must be escaped together.
            int length = char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]) ? 2 : 1;
            builder.Append(Uri.EscapeDataString(text.Substring(i, length)));
            i += length - 1;
            runStart = i + 1;
        }

        if (builder is null)
        {
            return text;
        }

        builder.Append(text, runStart, text.Length - runStart);
        return builder.ToString();
    }

    private static bool IsSafe(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
        or '-' or '.' or '_' or '~'
        or '!' or '$' or '\'' or '(' or ')' or '*' or ',' or ';' or ':' or '@' or '/' or '?' or '[' or ']';

    /// <summary>Decodes a query name or value: <c>+</c> is a space, then percent sequences are unescaped.</summary>
    public static string Decode(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));

    /// <summary>
    /// The raw <c>name=value</c> segments of a query string, in order, with a leading <c>?</c>, anything before
    /// it (a whole URL) and any fragment removed. Empty segments are dropped.
    /// </summary>
    public static string[] Segments(string query)
    {
        int hash = query.IndexOf('#');
        if (hash >= 0)
        {
            query = query[..hash];
        }

        int question = query.IndexOf('?');
        if (question >= 0)
        {
            query = query[(question + 1)..];
        }

        return query.Split('&', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>The decoded name and value of one raw segment; a segment without <c>=</c> has an empty value.</summary>
    public static (string Name, string Value) Parse(string segment)
    {
        int equals = segment.IndexOf('=');
        return equals < 0
            ? (Decode(segment), string.Empty)
            : (Decode(segment[..equals]), Decode(segment[(equals + 1)..]));
    }
}
