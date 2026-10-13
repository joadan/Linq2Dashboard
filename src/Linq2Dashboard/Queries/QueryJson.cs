using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Linq2Dashboard.Facets;
using Linq2Dashboard.Serialization;
using static Linq2Dashboard.Queries.QueryNames;

namespace Linq2Dashboard
{
    /// <summary>
    /// How <see cref="Dashboard{T}.QueryFromJson(string, QueryJsonOptions{T}?)"/> reads a query that comes from outside
    /// the application, from an agent or an HTTP client (design §10.6).
    /// </summary>
    /// <typeparam name="T">The dashboard's row type.</typeparam>
    public sealed class QueryJsonOptions<T>
    {
        /// <summary>
        /// Turns the query's <c>where</c> text into the predicate, for example with Gridify. The core never reads the
        /// text itself. Without a parser, a query with a <c>where</c> is refused. What the parser throws is wrapped in a
        /// <see cref="DashboardQueryException"/> at <c>where</c>.
        /// </summary>
        public Func<string, Expression<Func<T, bool>>>? Where { get; init; }

        /// <summary>A sentence on what the where accepts, such as its fields, added to the message when the where cannot be read.</summary>
        public string? WhereHelp { get; init; }

        /// <summary>
        /// The facet keys, metric keys and property names the query may use outside the where, selections included.
        /// Null allows every name the dashboard knows. This is where exposure to an agent or a client is decided.
        /// </summary>
        public IReadOnlyCollection<string>? Allowed { get; init; }
    }
}

namespace Linq2Dashboard.Queries
{
    /// <summary>
    /// Writes a <see cref="QueryResult"/> as JSON (design §10.6): a column header, rows as arrays in column order,
    /// values in each facet's own JSON form, intervals in the query-string notation, periods as their local start date.
    /// </summary>
    internal static class QueryJsonWriter
    {
        public static JsonObject Write(QueryResult result)
        {
            var columns = new JsonArray();
            foreach (QueryColumn column in result.Columns)
            {
                var json = new JsonObject
                {
                    ["key"] = column.Key,
                    ["name"] = column.Name,
                    ["role"] = JsonValues.CamelCase(column.Role),
                    ["kind"] = JsonValues.CamelCase(column.Kind),
                };
                if (column.Source is string source)
                {
                    json["source"] = source;
                }

                if (column.Aggregation is Aggregation aggregation)
                {
                    json["aggregation"] = JsonValues.CamelCase(aggregation);
                }

                if (column.Granularity is DateGranularity granularity)
                {
                    json["granularity"] = JsonValues.CamelCase(granularity);
                }

                columns.Add(json);
            }

            var rows = new JsonArray();
            foreach (QueryRow row in result.Rows)
            {
                var cells = new JsonArray();
                for (int c = 0; c < result.Columns.Count; c++)
                {
                    cells.Add(Cell(row[c], result.ColumnFacets[c]));
                }

                rows.Add(cells);
            }

            return new JsonObject
            {
                ["columns"] = columns,
                ["rows"] = rows,
                ["total"] = result.Total,
                ["cut"] = result.IsCut,
            };
        }

        private static JsonNode? Cell(object? cell, FacetIndex? facet) => cell switch
        {
            null => null,
            double number => JsonValue.Create(number),
            QueryValueCell value => Value(value, facet),
            IReadOnlyList<QueryValueCell> values => new JsonArray(values.Select(v => Value(v, facet)).ToArray()),
            RangeInterval interval => facet is not null
                ? facet.SerializeQuery(new RangeSelection([interval]))
                : JsonValue.Create(interval.ToString()),
            QueryPeriod period => period.PeriodStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DateTimeOffset instant => QueryValues.FormatInstant(instant),
            DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => JsonValues.Format(cell),
        };

        private static JsonNode? Value(QueryValueCell cell, FacetIndex? facet)
        {
            JsonNode? value = facet is not null ? facet.FormatValue(cell.Value) : JsonValues.Format(cell.Value);
            return cell.Label is null ? value : new JsonObject { ["value"] = value, ["label"] = cell.Label };
        }
    }

    /// <summary>
    /// Writes a <see cref="DashboardDescription"/> as JSON (design §10.7): facets, metrics and properties with only the
    /// members that have a value, values in each facet's own form, enum names in camel case like the rest of the JSON.
    /// </summary>
    internal static class DescriptionJsonWriter
    {
        public static JsonObject Write(DashboardDescription description, IReadOnlyDictionary<string, FacetIndex> facets)
        {
            var facetArray = new JsonArray();
            foreach (FacetDescription facet in description.Facets)
            {
                var json = new JsonObject { ["key"] = facet.Key, ["name"] = facet.Name, ["kind"] = JsonValues.CamelCase(facet.Kind) };
                Add(json, "description", facet.Description);
                Add(json, "valueCount", facet.ValueCount);
                if (facet.HasLabels)
                {
                    json["labelled"] = true;
                }

                if (facet.Values is IReadOnlyList<QueryValueCell> values)
                {
                    FacetIndex index = facets[facet.Key];
                    json["values"] = new JsonArray(values
                        .Select(v => v.Label is null
                            ? index.FormatValue(v.Value)
                            : new JsonObject { ["value"] = index.FormatValue(v.Value), ["label"] = v.Label })
                        .ToArray());
                }

                Add(json, "buckets", facet.BucketCount);
                Add(json, "min", facet.Min);
                Add(json, "max", facet.Max);
                Add(json, "granularity", facet.Granularity is DateGranularity g ? JsonValues.CamelCase(g) : null);
                Add(json, "timeZone", facet.TimeZone);
                if (facet.Presets is { Count: > 0 } presets)
                {
                    json["presets"] = new JsonArray(presets.Select(p => (JsonNode?)JsonValues.CamelCase(p)).ToArray());
                }

                facetArray.Add(json);
            }

            var metricArray = new JsonArray();
            foreach (MetricDescription metric in description.Metrics)
            {
                var json = new JsonObject { ["key"] = metric.Key, ["name"] = metric.Name, ["aggregation"] = JsonValues.CamelCase(metric.Aggregation) };
                Add(json, "description", metric.Description);
                metricArray.Add(json);
            }

            var propertyArray = new JsonArray();
            foreach (PropertyDescription property in description.Properties)
            {
                var json = new JsonObject
                {
                    ["name"] = property.Name,
                    ["kind"] = JsonValues.CamelCase(property.Kind),
                    ["uses"] = new JsonArray(property.Uses.Select(u => (JsonNode?)JsonValues.CamelCase(u)).ToArray()),
                };
                if (property.BehindFacet)
                {
                    json["behindFacet"] = true;
                }

                Add(json, "description", property.Description);
                if (property.Values is IReadOnlyList<string> names)
                {
                    json["values"] = new JsonArray(names.Select(n => (JsonNode?)n).ToArray());
                }

                propertyArray.Add(json);
            }

            return new JsonObject
            {
                ["facets"] = facetArray,
                ["metrics"] = metricArray,
                ["properties"] = propertyArray,
                ["periods"] = new JsonArray(description.Periods.Select(p => (JsonNode?)JsonValues.CamelCase(p)).ToArray()),
                ["limits"] = new JsonObject
                {
                    ["rows"] = description.MaxRows,
                    ["defaultRows"] = description.DefaultRows,
                    ["dimensions"] = description.MaxDimensions,
                },
            };
        }

        private static void Add(JsonObject json, string name, string? value)
        {
            if (value is not null)
            {
                json[name] = value;
            }
        }

        private static void Add(JsonObject json, string name, int? value)
        {
            if (value is int number)
            {
                json[name] = number;
            }
        }

        private static void Add(JsonObject json, string name, double? value)
        {
            if (value is double number)
            {
                json[name] = number;
            }
        }
    }

    /// <summary>
    /// Reads a query from JSON strictly (design §10.6, amended by §10.9): every part as an object or a short text form,
    /// selections as an object or as query-string text, the where through the application's parser, names limited to
    /// <see cref="QueryJsonOptions{T}.Allowed"/>. Anything that cannot be read throws a <see cref="DashboardQueryException"/>
    /// whose path names the part.
    /// </summary>
    internal static class QueryJsonReader
    {
        private static readonly string[] Parts = ["selections", "where", "groupBy", "values", "columns", "orderBy", "take"];

        private static readonly Dictionary<string, Aggregation> AggregationNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["count"] = Aggregation.Count,
            ["sum"] = Aggregation.Sum,
            ["avg"] = Aggregation.Average,
            ["average"] = Aggregation.Average,
            ["min"] = Aggregation.Min,
            ["max"] = Aggregation.Max,
            ["distinct"] = Aggregation.Distinct,
        };

        public static DashboardQuery<T> Read<T>(Dashboard<T> dashboard, string json, QueryJsonOptions<T>? options)
        {
            JsonNode? root;
            try
            {
                root = JsonNode.Parse(json);
            }
            catch (JsonException e)
            {
                throw new DashboardQueryException($"The query is not valid JSON: {e.Message}", null, e);
            }

            return root is JsonObject query
                ? Read(dashboard, query, options)
                : throw new DashboardQueryException($"A query is a JSON object with any of: {string.Join(", ", Parts)}.");
        }

        public static DashboardQuery<T> Read<T>(Dashboard<T> dashboard, JsonObject json, QueryJsonOptions<T>? options)
        {
            foreach ((string name, _) in json)
            {
                if (!Parts.Contains(name, StringComparer.Ordinal))
                {
                    throw Error($"A query has no '{name}'.{Suggest(name, Parts)} It has: {string.Join(", ", Parts)}.", name);
                }
            }

            Selections selections = ReadSelections(dashboard, json["selections"]);
            Expression<Func<T, bool>>? where = ReadWhere(json["where"], options);
            IReadOnlyList<Dimension> groupBy = ReadList(json["groupBy"], "groupBy", ReadDimension);
            IReadOnlyList<Dimension> columns = ReadList(json["columns"], "columns", ReadDimension);
            IReadOnlyList<QueryValue> values = ReadList(json["values"], "values", (node, path) => ReadValue(dashboard, node, path));
            IEnumerable<string> keys = groupBy.Select(d => d.Key).Concat(values.Select(v => v.Key)).Concat(columns.Select(c => c.Key));
            QueryOrder? orderBy = ReadOrder(dashboard, json["orderBy"], keys.ToHashSet(StringComparer.Ordinal));
            int? take = ReadTake(json["take"]);

            if (options?.Allowed is IReadOnlyCollection<string> allowed)
            {
                CheckAllowed(allowed, selections.Select(s => (s.Key, $"selections.{s.Key}")));
                CheckAllowed(allowed, groupBy.Select((d, i) => (d.Name, $"groupBy[{i}]")));
                CheckAllowed(allowed, columns.Select((c, i) => (c.Name, $"columns[{i}]")));
                CheckAllowed(allowed, values
                    .Select((v, i) => (Name: v.MetricKey ?? v.Of, Path: $"values[{i}]"))
                    .Where(p => p.Name is not null)
                    .Select(p => (p.Name!, p.Path)));
            }

            var query = new DashboardQuery<T>
            {
                Selections = selections,
                Where = where,
                GroupBy = groupBy,
                Values = values,
                Columns = columns,
                OrderBy = orderBy,
                Take = take,
            };

            QueryRunner.Check(dashboard, query);
            return query;
        }

        /// <summary>The object form of design §2.5, or the query-string form a page's URL carries (design §10.9), both strict.</summary>
        private static Selections ReadSelections<T>(Dashboard<T> dashboard, JsonNode? node)
        {
            try
            {
                return node switch
                {
                    null => Selections.Empty,
                    JsonObject shape => dashboard.Serializer.FromJsonObject(shape, strict: true),
                    JsonValue text when text.TryGetValue(out string? query) => dashboard.Serializer.FromQueryString(query, string.Empty, strict: true),
                    _ => throw Error("Selections are an object with one property per facet, or query-string text such as \"Country=SE,NO&Status=Open\".", "selections"),
                };
            }
            catch (SelectionFormatException e)
            {
                throw new DashboardQueryException(e.Message, $"selections.{e.Key}", e);
            }
        }

        private static Expression<Func<T, bool>>? ReadWhere<T>(JsonNode? node, QueryJsonOptions<T>? options)
        {
            if (node is null)
            {
                return null;
            }

            if (node is not JsonValue value || !value.TryGetValue(out string? text))
            {
                throw Error("The where is text, read by the application's parser.", "where");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            Func<string, Expression<Func<T, bool>>> parse = options?.Where
                ?? throw Error("This query reader takes no where; narrow the rows with selections instead.", "where");
            try
            {
                return parse(text);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                string help = options.WhereHelp is string sentence ? " " + sentence : string.Empty;
                throw new DashboardQueryException($"The where '{text}' cannot be read: {e.Message.TrimEnd('.')}.{help}", "where", e);
            }
        }

        private static IReadOnlyList<TPart> ReadList<TPart>(JsonNode? node, string path, Func<JsonNode?, string, TPart> read) => node switch
        {
            null => [],
            JsonArray array => array.Select((element, i) => read(element, $"{path}[{i}]")).ToArray(),
            _ => throw Error($"The query's {path} is a list.", path),
        };

        /// <summary><c>"Country"</c>, <c>"OrderDate:Month"</c>, or <c>{ "name" | "property", "period", "as" }</c>.</summary>
        private static Dimension ReadDimension(JsonNode? node, string path)
        {
            if (node is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0)
            {
                (string named, DateGranularity? textPeriod) = SplitPeriod(text);
                return textPeriod is DateGranularity granularity ? Dimension.Of(named, granularity) : Dimension.Of(named);
            }

            if (node is not JsonObject json)
            {
                throw Error("A dimension is a name such as \"Country\" or \"OrderDate:Month\", or an object { \"name\", \"period\" }; { \"property\" } names a property behind a facet's name.", path);
            }

            Only(json, path, "name", "property", "period", "as");
            string? name = Text(json, "name", path);
            string? property = Text(json, "property", path);
            if ((name is null) == (property is null))
            {
                throw Error("A dimension has a \"name\" or a \"property\", not both.", path);
            }

            DateGranularity? period = Text(json, "period", path) is string periodText
                ? ParsePeriod(periodText) ?? throw Error($"'{periodText}' is not a period. Periods: {string.Join(", ", PeriodNames)}.", path)
                : null;
            Dimension dimension = (property, period) switch
            {
                (string p, DateGranularity g) => Dimension.Property(p, g),
                (string p, null) => Dimension.Property(p),
                (null, DateGranularity g) => Dimension.Of(name!, g),
                _ => Dimension.Of(name!),
            };
            return Text(json, "as", path) is string key ? dimension.As(key) : dimension;
        }

        /// <summary><c>"revenue"</c> (a metric), <c>"count"</c>, <c>"avg:Amount"</c>, or <c>{ "metric" | "aggregation", "of", "as" }</c>.</summary>
        private static QueryValue ReadValue<T>(Dashboard<T> dashboard, JsonNode? node, string path)
        {
            if (node is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0)
            {
                return ValueFromText(dashboard, text, path);
            }

            if (node is not JsonObject json)
            {
                throw Error("A value is a metric key such as \"revenue\", \"count\", an aggregation such as \"avg:Amount\", or an object { \"metric\" } or { \"aggregation\", \"of\" }.", path);
            }

            Only(json, path, "metric", "aggregation", "of", "as");
            string? metric = Text(json, "metric", path);
            string? aggregationText = Text(json, "aggregation", path);
            QueryValue result;
            if (metric is not null)
            {
                if (aggregationText is not null || json.ContainsKey("of"))
                {
                    throw Error("A value reads a \"metric\" or computes an \"aggregation\", not both.", path);
                }

                result = QueryValue.Metric(metric);
            }
            else
            {
                Aggregation aggregation = aggregationText is not null && AggregationNames.TryGetValue(aggregationText, out Aggregation known)
                    ? known
                    : throw Error($"A value needs a \"metric\" or an \"aggregation\": {string.Join(", ", AggregationNames.Keys)}.", path);
                result = Aggregate(aggregation, Text(json, "of", path), path);
            }

            return Text(json, "as", path) is string key ? result.As(key) : result;
        }

        private static QueryValue ValueFromText<T>(Dashboard<T> dashboard, string text, string path)
        {
            int colon = text.IndexOf(':');
            if (colon > 0 && AggregationNames.TryGetValue(text[..colon], out Aggregation aggregation))
            {
                return Aggregate(aggregation, text[(colon + 1)..], path);
            }

            if (!dashboard.TryGetMetricIndex(text, out _) && string.Equals(text, "count", StringComparison.OrdinalIgnoreCase))
            {
                return QueryValue.Count();
            }

            return QueryValue.Metric(text);
        }

        private static QueryValue Aggregate(Aggregation aggregation, string? of, string path)
        {
            if (aggregation == Aggregation.Count)
            {
                return string.IsNullOrEmpty(of) ? QueryValue.Count() : throw Error("A count counts rows and takes no name.", path);
            }

            if (string.IsNullOrEmpty(of))
            {
                throw Error($"{aggregation} needs the name of what it aggregates, as in \"{JsonValues.CamelCase(aggregation)}:Amount\".", path);
            }

            return aggregation switch
            {
                Aggregation.Sum => QueryValue.Sum(of),
                Aggregation.Average => QueryValue.Average(of),
                Aggregation.Min => QueryValue.Min(of),
                Aggregation.Max => QueryValue.Max(of),
                _ => QueryValue.Distinct(of),
            };
        }

        /// <summary><c>"-revenue"</c> for descending, <c>"revenue"</c> or <c>"+revenue"</c> for ascending, or <c>{ "key", "descending" }</c>. The key may be written as the part's text form.</summary>
        private static QueryOrder? ReadOrder<T>(Dashboard<T> dashboard, JsonNode? node, HashSet<string> keys)
        {
            string key;
            bool descending;
            switch (node)
            {
                case null:
                    return null;
                case JsonValue value when value.TryGetValue(out string? text) && text.Length > 0:
                    descending = text[0] == '-';
                    key = text[0] is '-' or '+' ? text[1..] : text;
                    break;
                case JsonObject json:
                    Only(json, "orderBy", "key", "descending");
                    key = Text(json, "key", "orderBy") ?? throw Error("An order has a \"key\".", "orderBy");
                    descending = json["descending"] is JsonValue flag && flag.TryGetValue(out bool b) ? b
                        : json["descending"] is null ? false
                        : throw Error("\"descending\" is true or false.", "orderBy");
                    break;
                default:
                    throw Error("An order is a column key such as \"-revenue\" (descending) or \"revenue\", or { \"key\", \"descending\" }.", "orderBy");
            }

            if (!keys.Contains(key) && key.Length > 0)
            {
                // A text form of a part orders by that part's key: "-avg:Amount" by Average(Amount), "OrderDate:Month" by Month(OrderDate).
                string fromValue = ValueFromText(dashboard, key, "orderBy").Key;
                (string name, DateGranularity? period) = SplitPeriod(key);
                string fromDimension = period is DateGranularity g ? Dimension.Of(name, g).Key : key;
                key = keys.Contains(fromValue) ? fromValue : keys.Contains(fromDimension) ? fromDimension : key;
            }

            return descending ? QueryOrder.Descending(key) : QueryOrder.Ascending(key);
        }

        private static int? ReadTake(JsonNode? node) => node switch
        {
            null => null,
            JsonValue value when value.TryGetValue(out int take) => take,
            JsonValue value when value.TryGetValue(out double number) && number == Math.Floor(number) && number <= int.MaxValue && number >= int.MinValue => (int)number,
            _ => throw Error("Take is a whole number.", "take"),
        };

        private static void CheckAllowed(IReadOnlyCollection<string> allowed, IEnumerable<(string Name, string Path)> names)
        {
            foreach ((string name, string path) in names)
            {
                if (!allowed.Contains(name, StringComparer.Ordinal))
                {
                    throw Error($"'{name}' is not available here.{Suggest(name, allowed)} Available: {string.Join(", ", allowed)}.", path);
                }
            }
        }

        private static readonly string[] PeriodNames = Enum.GetNames<DateGranularity>().Select(n => n.ToLowerInvariant()).ToArray();

        /// <summary><c>OrderDate:Month</c> is a period when the text after the last colon names one; otherwise the whole text is a name.</summary>
        private static (string Name, DateGranularity? Period) SplitPeriod(string text)
        {
            int colon = text.LastIndexOf(':');
            return colon > 0 && ParsePeriod(text[(colon + 1)..]) is DateGranularity period ? (text[..colon], period) : (text, null);
        }

        private static DateGranularity? ParsePeriod(string text) =>
            text.Length > 0 && text.All(char.IsLetter) && Enum.TryParse(text, ignoreCase: true, out DateGranularity period) ? period : null;

        private static string? Text(JsonObject json, string property, string path) => json[property] switch
        {
            null => null,
            JsonValue value when value.TryGetValue(out string? text) && text.Length > 0 => text,
            _ => throw Error($"\"{property}\" is text.", path),
        };

        private static void Only(JsonObject json, string path, params string[] properties)
        {
            foreach ((string name, _) in json)
            {
                if (!properties.Contains(name, StringComparer.Ordinal))
                {
                    throw Error($"This part has no '{name}'.{Suggest(name, properties)} It has: {string.Join(", ", properties)}.", path);
                }
            }
        }
    }
}
