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
/// Reading either is lenient: unknown facet keys, values that cannot be read, and shapes that do not fit
/// the facet's kind are dropped, so a stale bookmark degrades to fewer selections rather than an
/// error. Only text that is not JSON at all throws.
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

    /// <summary>Reads selections from JSON text. Throws <see cref="JsonException"/> only when the text is not valid JSON.</summary>
    public Selections FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? node = JsonNode.Parse(json);
        return node is JsonObject root ? FromJsonObject(root) : Selections.Empty;
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

    /// <summary>Reads selections from already decoded parameters, with the same leniency as <see cref="FromQueryString"/>.</summary>
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
