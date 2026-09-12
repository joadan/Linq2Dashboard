using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Linq2Dashboard.Facets;

namespace Linq2Dashboard;

/// <summary>
/// Writes and reads <see cref="Selections"/> as JSON (design §2.5). Built by the dashboard because
/// reading needs each facet's value type. Otherwise stateless and thread-safe.
/// </summary>
/// <remarks>
/// The shape is one property per facet key:
/// <code>
/// {
///   "Country":   { "values": ["SE", null] },
///   "Amount":    { "from": 100, "to": 500, "toInclusive": false },
///   "OrderDate": { "preset": "last30Days" }
/// }
/// </code>
/// Reading is lenient: unknown facet keys, values that cannot be read, and shapes that do not fit
/// the facet's kind are dropped, so a stale bookmark degrades to fewer selections rather than an
/// error. Only text that is not JSON at all throws.
/// </remarks>
public sealed class SelectionSerializer
{
    // Relaxed escaping keeps "+01:00" and non-ASCII values readable. The output is data for storage
    // and URLs, never embedded raw into HTML, which is the case the default escaping guards against.
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly IReadOnlyDictionary<string, FacetIndex> facets;

    internal SelectionSerializer(IEnumerable<FacetIndex> facets)
    {
        this.facets = facets.ToDictionary(f => f.Key, StringComparer.Ordinal);
    }

    public string ToJson(Selections selections, bool indented = false) =>
        ToJsonObject(selections).ToJsonString(indented ? Indented : Compact);

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
}
