using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Linq2Dashboard.Facets;
using Linq2Dashboard.Serialization;

namespace Linq2Dashboard;

/// <summary>
/// Writes and reads <see cref="Selections"/> as JSON, for bookmarks, and as query-string parameters, for
/// URLs (design §2.5). Built by the dashboard because reading needs each facet's value type. Otherwise
/// stateless and thread-safe.
/// </summary>
/// <remarks>
/// The JSON shape is one property per facet key:
/// <code>
/// {
///   "Country":   { "values": ["SE", null] },
///   "Amount":    { "from": 100, "to": 500, "toInclusive": false },
///   "OrderDate": { "preset": "last30Days" }
/// }
/// </code>
/// The query-string form is one readable parameter per facet key, which a person or another page can write by hand:
/// <code>
/// Country=SE,null&amp;Amount=[100..500)&amp;OrderDate=last30Days
/// </code>
/// Reading either is lenient by default: unknown facet keys, values that cannot be read, and shapes that do not
/// fit the facet's kind are dropped, so a stale bookmark degrades to fewer selections rather than an error. Only
/// text that is not JSON at all throws. Read strictly, for input a query depends on, anything that would be dropped
/// throws a <see cref="FormatException"/> instead (design §10.9).
/// </remarks>
public sealed class SelectionSerializer
{
    // Relaxed escaping keeps "+01:00" and non-ASCII values readable. The output is data for storage
    // and URLs, never embedded raw into HTML, which is the case the default escaping guards against.
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly FacetIndex[] facetOrder;
    private readonly IReadOnlyDictionary<string, FacetIndex> facets;

    internal SelectionSerializer(IEnumerable<FacetIndex> facets)
    {
        facetOrder = facets.ToArray();
        this.facets = facetOrder.ToDictionary(f => f.Key, StringComparer.Ordinal);
    }

    /// <summary>Writes <paramref name="selections"/> as JSON text, for a bookmark or a URL (concept §4.9).</summary>
    public string ToJson(Selections selections, bool indented = false) =>
        ToJsonObject(selections).ToJsonString(indented ? Indented : Compact);

    /// <summary>Writes <paramref name="selections"/> as a JSON object, for embedding in a larger document.</summary>
    public JsonObject ToJsonObject(Selections selections)
    {
        ArgumentNullException.ThrowIfNull(selections);

        var json = new JsonObject();
        foreach ((string key, Selection selection) in selections)
        {
            if (!facets.TryGetValue(key, out FacetIndex? facet))
            {
                throw new ArgumentException($"Unknown facet key '{key}'.", nameof(selections));
            }

            json[key] = facet.Serialize(selection);
        }

        return json;
    }

    /// <summary>
    /// Writes a query's answer as JSON (design §10.6): a column header, rows as arrays in column order, values in each
    /// facet's own form (with <c>{ "value", "label" }</c> where the facet has labels), intervals in the query-string
    /// notation such as <c>[100..500)</c>, periods as their local start date, instants in short ISO 8601, and null for a
    /// null group or "no value". Compact by default, since an agent pays for every character.
    /// </summary>
    public string ToJson(QueryResult result, bool indented = false) =>
        ToJsonObject(result).ToJsonString(indented ? Indented : Compact);

    /// <summary>Writes a query's answer as a JSON object, for embedding in a larger document.</summary>
    public JsonObject ToJsonObject(QueryResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return Queries.QueryJsonWriter.Write(result);
    }

    /// <summary>
    /// Writes what a query can ask (design §10.7) as JSON for a client or an agent: facets with their kind and what helps a
    /// question, metrics, properties with their kind and uses, the periods and the limits. Values are in each facet's own
    /// form; members without a value are left out, so the text stays short.
    /// </summary>
    public string ToJson(DashboardDescription description, bool indented = false) =>
        ToJsonObject(description).ToJsonString(indented ? Indented : Compact);

    /// <summary>Writes what a query can ask as a JSON object, for embedding in a larger document such as a tool's description.</summary>
    public JsonObject ToJsonObject(DashboardDescription description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return Queries.DescriptionJsonWriter.Write(description, facets);
    }

    /// <summary>Reads selections from JSON text. Throws <see cref="JsonException"/> only when the text is not valid JSON.</summary>
    public Selections FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? node = JsonNode.Parse(json);
        return node is JsonObject root ? FromJsonObject(root) : Selections.Empty;
    }

    /// <summary>
    /// Reads selections from JSON text; with <paramref name="strict"/>, an unknown facet key, a shape that does not fit
    /// the facet or a value that cannot be read throws a <see cref="FormatException"/> naming it, instead of being dropped.
    /// </summary>
    public Selections FromJson(string json, bool strict)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (!strict)
        {
            return FromJson(json);
        }

        return JsonNode.Parse(json) is JsonObject root
            ? FromJsonObject(root, strict)
            : throw new FormatException("Selections are a JSON object with one property per facet key.");
    }

    /// <summary>Reads selections from a JSON object, strictly when <paramref name="strict"/> is set (see <see cref="FromJson(string, bool)"/>).</summary>
    public Selections FromJsonObject(JsonObject json, bool strict)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (!strict)
        {
            return FromJsonObject(json);
        }

        Selections selections = Selections.Empty;
        foreach ((string key, JsonNode? node) in json)
        {
            selections = selections.With(key, ReadStrict(key, node));
        }

        return selections;
    }

    /// <summary>Reads selections from a JSON object, leniently: unknown facets and values that cannot be read are dropped.</summary>
    public Selections FromJsonObject(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);

        Selections selections = Selections.Empty;
        foreach ((string key, JsonNode? node) in json)
        {
            if (node is not JsonObject shape || !facets.TryGetValue(key, out FacetIndex? facet))
            {
                continue;
            }

            Selection? selection = facet.Deserialize(shape);
            if (selection is not null)
            {
                selections = selections.With(key, selection);
            }
        }

        return selections;
    }

    /// <summary>
    /// Writes <paramref name="selections"/> as query-string parameters, one per facet, named
    /// <paramref name="prefix"/> + facet key (design §2.5). Every facet is present: a selected one with its
    /// value, an unselected one with <c>null</c>, so merging the result into a URL removes what was cleared.
    /// The shape is what <c>NavigationManager.GetUriWithQueryParameters</c> takes.
    /// </summary>
    public IReadOnlyDictionary<string, string?> ToQuery(Selections selections, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(selections);
        ArgumentNullException.ThrowIfNull(prefix);

        var query = new Dictionary<string, string?>(facetOrder.Length, StringComparer.Ordinal);
        foreach (FacetIndex facet in facetOrder)
        {
            query[prefix + facet.Key] = selections.TryGet(facet.Key, out Selection? selection) ? facet.SerializeQuery(selection) : null;
        }

        foreach ((string key, _) in selections)
        {
            if (!facets.ContainsKey(key))
            {
                throw new ArgumentException($"Unknown facet key '{key}'.", nameof(selections));
            }
        }

        return query;
    }

    /// <summary>
    /// Writes <paramref name="selections"/> as a query string without the leading <c>?</c>, such as
    /// <c>Country=SE,NO&amp;Amount=100..500</c>, with only the safe characters percent-encoded so the URL stays
    /// readable. When <paramref name="existingQuery"/> is given (a query string or a whole URL), every parameter
    /// in it that is not one of this dashboard's facets under <paramref name="prefix"/> is kept, in place, so a
    /// page's own parameters survive and the facets' are replaced.
    /// </summary>
    public string ToQueryString(Selections selections, string prefix = "", string? existingQuery = null)
    {
        IReadOnlyDictionary<string, string?> query = ToQuery(selections, prefix);
        var parts = new List<string>();
        if (existingQuery is not null)
        {
            foreach (string segment in QueryValues.Segments(existingQuery))
            {
                if (!query.ContainsKey(QueryValues.Parse(segment).Name))
                {
                    parts.Add(segment);
                }
            }
        }

        foreach (FacetIndex facet in facetOrder)
        {
            if (query[prefix + facet.Key] is string value)
            {
                parts.Add(QueryValues.Encode(prefix + facet.Key) + "=" + QueryValues.Encode(value));
            }
        }

        return string.Join('&', parts);
    }

    /// <summary>
    /// Reads selections from a query string, a whole URL, or a query string with a leading <c>?</c>, leniently:
    /// parameters that are not <paramref name="prefix"/> + a facet key are ignored, values that cannot be read
    /// are dropped, and the last of several parameters with the same name wins.
    /// </summary>
    public Selections FromQueryString(string query, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(query);
        return FromQuery(QueryValues.Segments(query).Select(segment =>
        {
            (string name, string value) = QueryValues.Parse(segment);
            return new KeyValuePair<string, string?>(name, value);
        }), prefix);
    }

    /// <summary>
    /// Reads selections from a query string; with <paramref name="strict"/>, every parameter under
    /// <paramref name="prefix"/> must name a facet once and every value in it must read, or a
    /// <see cref="FormatException"/> names what does not (design §10.9). Parameters outside the prefix are ignored.
    /// </summary>
    public Selections FromQueryString(string query, string prefix, bool strict)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(prefix);
        if (!strict)
        {
            return FromQueryString(query, prefix);
        }

        Selections selections = Selections.Empty;
        foreach (string segment in QueryValues.Segments(query))
        {
            (string name, string value) = QueryValues.Parse(segment);
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            string key = name[prefix.Length..];
            if (selections.TryGet(key, out _))
            {
                throw new SelectionFormatException(key, $"Facet '{key}' is named twice; give its values once, separated by commas.");
            }

            selections = selections.With(key, ReadQueryStrict(key, value));
        }

        return selections;
    }

    /// <summary>One facet's JSON shape, refusing what the lenient reader would drop.</summary>
    private Selection ReadStrict(string key, JsonNode? node)
    {
        FacetIndex facet = FacetOrThrow(key);
        string[] properties = ShapeProperties(facet.Kind);
        if (node is not JsonObject shape)
        {
            throw new SelectionFormatException(key, $"The selection of facet '{key}' is an object with {string.Join(", ", properties)}.");
        }

        foreach ((string property, _) in shape)
        {
            if (!properties.Contains(property, StringComparer.Ordinal))
            {
                throw new SelectionFormatException(key, $"The selection of facet '{key}' has no '{property}'; a {facet.Kind} facet's selection has {string.Join(", ", properties)}.");
            }
        }

        if (shape["values"] is JsonArray named)
        {
            // A label stands for its value (design §10.6); the caller's object is left as it is.
            var values = new JsonArray(named.Select(value => LabelledValue(facet, key, value) ?? value?.DeepClone()).ToArray());
            shape = (JsonObject)shape.DeepClone();
            shape["values"] = values;
            foreach (JsonNode? value in values)
            {
                if (facet.Deserialize(new JsonObject { ["values"] = new JsonArray(value?.DeepClone()) }) is null)
                {
                    throw new SelectionFormatException(key, $"{Show(value)} is not a value of facet '{key}'.");
                }
            }
        }

        if (shape["intervals"] is JsonArray intervals)
        {
            foreach (JsonNode? interval in intervals)
            {
                bool readable = interval is JsonObject part && (part.Count == 0 || facet.Deserialize((JsonObject)part.DeepClone()) is not null);
                if (!readable)
                {
                    throw new SelectionFormatException(key, $"The interval {Show(interval)} of facet '{key}' cannot be read.");
                }
            }
        }

        return facet.Deserialize(shape)
            ?? throw new SelectionFormatException(key, $"The selection of facet '{key}' selects nothing or cannot be read: {shape.ToJsonString(Compact)}.");
    }

    /// <summary>One facet's query-string value, refusing what the lenient reader would drop.</summary>
    private Selection ReadQueryStrict(string key, string value)
    {
        FacetIndex facet = FacetOrThrow(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new SelectionFormatException(key, $"Facet '{key}' has no value.");
        }

        if (facet.Kind != FacetKind.Text)
        {
            var parts = new List<string>();
            bool labelled = false;
            foreach (string? token in QueryValues.SplitTokens(value))
            {
                // A label stands for its value (design §10.6), written as the value's own text.
                string? text = LabelledValue(facet, key, token is null ? null : JsonValue.Create(token)) switch
                {
                    null => token,
                    JsonValue named when named.GetValueKind() == System.Text.Json.JsonValueKind.String => named.GetValue<string>(),
                    JsonNode named => named.ToJsonString(),
                };
                labelled |= text != token;
                string part = text is null ? QueryValues.NullToken : QueryValues.EscapeToken(text);
                if (facet.DeserializeQuery(part) is null)
                {
                    throw new SelectionFormatException(key, $"'{token ?? QueryValues.NullToken}' is not a value of facet '{key}' ({facet.Kind}).");
                }

                parts.Add(part);
            }

            if (labelled)
            {
                value = string.Join(',', parts);
            }
        }

        return facet.DeserializeQuery(value)
            ?? throw new SelectionFormatException(key, $"'{value}' cannot be read as a selection of facet '{key}' ({facet.Kind}).");
    }

    /// <summary>The value a token names by its label, or null; a label of several values is an error at the facet.</summary>
    private static JsonNode? LabelledValue(FacetIndex facet, string key, JsonNode? token)
    {
        try
        {
            return facet.ValueOfLabel(token);
        }
        catch (FormatException e)
        {
            throw new SelectionFormatException(key, e.Message);
        }
    }

    private FacetIndex FacetOrThrow(string key) =>
        facets.TryGetValue(key, out FacetIndex? facet)
            ? facet
            : throw new SelectionFormatException(key, $"'{key}' is not a facet of this dashboard. Facets: {string.Join(", ", facetOrder.Select(f => f.Key))}.");

    /// <summary>The properties each kind's JSON shape may have (design §2.5).</summary>
    private static string[] ShapeProperties(FacetKind kind) => kind switch
    {
        FacetKind.Range => ["from", "to", "fromInclusive", "toInclusive", "intervals", "includeNull", "onlyNull"],
        FacetKind.Date => ["from", "to", "preset", "intervals", "includeNull", "onlyNull"],
        FacetKind.Text => ["text"],
        _ => ["values"],
    };

    private static string Show(JsonNode? node) => node is null ? "null" : node.ToJsonString(Compact);

    /// <summary>Reads selections from already decoded parameters, with the same leniency as <see cref="FromQueryString(string, string)"/>.</summary>
    public Selections FromQuery(IEnumerable<KeyValuePair<string, string?>> parameters, string prefix = "")
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(prefix);

        Selections selections = Selections.Empty;
        foreach ((string name, string? value) in parameters)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal) || !facets.TryGetValue(name[prefix.Length..], out FacetIndex? facet))
            {
                continue;
            }

            Selection? selection = value is null ? null : facet.DeserializeQuery(value);
            selections = selection is null ? selections.Clear(facet.Key) : selections.With(facet.Key, selection);
        }

        return selections;
    }
}

/// <summary>A selection read strictly that cannot be read, with the facet key it names (design §10.9).</summary>
internal sealed class SelectionFormatException(string key, string message) : FormatException(message)
{
    public string Key { get; } = key;
}
