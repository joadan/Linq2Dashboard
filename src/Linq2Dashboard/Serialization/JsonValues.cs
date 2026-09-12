using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Linq2Dashboard.Serialization;

/// <summary>Shared JSON helpers for the selection shapes in design §2.5.</summary>
internal static class JsonValues
{
    /// <summary>
    /// Default formatting of a facet value: strings, booleans and numbers as themselves, enums by
    /// name, <see cref="Guid"/> and the date and time types as ISO 8601 strings, anything else via
    /// invariant <c>ToString</c>.
    /// </summary>
    public static JsonNode? Format(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        byte or sbyte or short or ushort or int or uint or long => JsonValue.Create(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
        ulong u => JsonValue.Create(u),
        float f => JsonValue.Create((double)f),
        double d => JsonValue.Create(d),
        decimal m => JsonValue.Create(m),
        Enum e => JsonValue.Create(e.ToString()),
        Guid g => JsonValue.Create(g.ToString()),
        DateTimeOffset dto => JsonValue.Create(dto.ToString("O", CultureInfo.InvariantCulture)),
        DateTime dt => JsonValue.Create(dt.ToString("O", CultureInfo.InvariantCulture)),
        DateOnly date => JsonValue.Create(date.ToString("O", CultureInfo.InvariantCulture)),
        TimeOnly time => JsonValue.Create(time.ToString("O", CultureInfo.InvariantCulture)),
        char c => JsonValue.Create(c.ToString()),
        _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    /// <summary>
    /// A JSON primitive as the plain CLR value it most naturally is: string, bool, decimal or double.
    /// Objects and arrays are not primitives and return false.
    /// </summary>
    public static bool TryToPrimitive(JsonNode? node, out object? value)
    {
        value = null;
        if (node is null)
        {
            return true;
        }

        if (node is not JsonValue jsonValue)
        {
            return false;
        }

        switch (jsonValue.GetValueKind())
        {
            case JsonValueKind.String:
                value = jsonValue.GetValue<string>();
                return true;
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            case JsonValueKind.Number:
                if (jsonValue.TryGetValue(out decimal m))
                {
                    value = m;
                    return true;
                }

                if (jsonValue.TryGetValue(out double d))
                {
                    value = d;
                    return true;
                }

                // In-memory nodes of another numeric type: go through the text.
                if (decimal.TryParse(jsonValue.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out m))
                {
                    value = m;
                    return true;
                }

                return false;
            default:
                return false;
        }
    }

    public static bool TryGetDouble(JsonObject json, string name, out double? value)
    {
        value = null;
        if (!json.TryGetPropertyValue(name, out JsonNode? node) || node is null)
        {
            return true;
        }

        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue(out double d) && !double.IsNaN(d))
        {
            value = d;
            return true;
        }

        return false;
    }

    public static bool TryGetBool(JsonObject json, string name, bool fallback, out bool value)
    {
        value = fallback;
        if (!json.TryGetPropertyValue(name, out JsonNode? node) || node is null)
        {
            return true;
        }

        if (node is JsonValue v && v.GetValueKind() is JsonValueKind.True or JsonValueKind.False)
        {
            value = v.GetValue<bool>();
            return true;
        }

        return false;
    }

    public static bool TryGetInstant(JsonObject json, string name, out DateTimeOffset? value)
    {
        value = null;
        if (!json.TryGetPropertyValue(name, out JsonNode? node) || node is null)
        {
            return true;
        }

        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String
            && DateTimeOffset.TryParse(v.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    public static bool TryGetEnum<TEnum>(JsonObject json, string name, out TEnum? value) where TEnum : struct, Enum
    {
        value = null;
        if (!json.TryGetPropertyValue(name, out JsonNode? node) || node is null)
        {
            return true;
        }

        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.String
            && Enum.TryParse(v.GetValue<string>(), ignoreCase: true, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    public static string Instant(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Enum name in camelCase, the JSON convention: <c>Last30Days</c> becomes <c>last30Days</c>.</summary>
    public static string CamelCase<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        string name = value.ToString();
        return name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }
}

/// <summary>An application-supplied way to write and read one facet's values as strings (design §2.5).</summary>
internal sealed class ValueFormatter<TValue>
{
    public ValueFormatter(Func<TValue, string> format, Func<string, TValue> parse)
    {
        Format = format;
        Parse = parse;
    }

    public Func<TValue, string> Format { get; }

    public Func<string, TValue> Parse { get; }
}
