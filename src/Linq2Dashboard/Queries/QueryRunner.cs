using Linq2Dashboard.Facets;
using Linq2Dashboard.Indexing;
using Linq2Dashboard.Metrics;

namespace Linq2Dashboard.Queries;

/// <summary>
/// Answers a <see cref="DashboardQuery{T}"/> over the dashboard's own columns (design §10.3): resolve and
/// check every part, take the matching set, give each matching row its group, accumulate the values per
/// group, then order, cut and build the cells. Reads the dashboard and leaves nothing behind.
/// </summary>
internal static class QueryRunner
{
    /// <summary>Up to this many combinations of the dimensions' codes, a group is found by array index; beyond it, through a hash.</summary>
    private const long DenseGroupLimit = 1 << 20;

    /// <summary>The cancellation token is checked once per this many rows (design §10.4).</summary>
    private const int CancellationStride = 1 << 16;

    private static readonly int[] NullCode = [0];

    public static QueryResult Run<T>(Dashboard<T> dashboard, DashboardQuery<T> query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        Plan plan = Plan.Resolve(dashboard, query);
        cancellationToken.ThrowIfCancellationRequested();

        RowSet rows = dashboard.MatchingRows(query.Selections);
        cancellationToken.ThrowIfCancellationRequested();

        Groups groups = Groups.Collect(plan, rows, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var values = new double?[groups.Count][];
        for (int g = 0; g < groups.Count; g++)
        {
            values[g] = new double?[plan.Values.Length];
            for (int v = 0; v < plan.Values.Length; v++)
            {
                values[g][v] = plan.Values[v].Read(groups, g);
            }
        }

        int[] order = Order(plan, groups, values, query.Take);
        var cells = new object?[order.Length][];
        int dimensions = plan.Dimensions.Length;
        for (int i = 0; i < order.Length; i++)
        {
            int g = order[i];
            var row = new object?[plan.Columns.Length];
            for (int d = 0; d < dimensions; d++)
            {
                int code = groups.Code(d, g);
                row[d] = code == 0 ? null : plan.Dimensions[d].Cell(code);
            }

            for (int v = 0; v < plan.Values.Length; v++)
            {
                row[dimensions + v] = values[g][v];
            }

            cells[i] = row;
        }

        return new QueryResult(plan.Columns, cells, groups.Count);
    }

    /// <summary>Group indexes in answer order, cut to <paramref name="take"/> with a heap when it cuts (design §10.3).</summary>
    private static int[] Order(Plan plan, Groups groups, double?[][] values, int? take)
    {
        Comparison<int> compare = Comparison(plan, groups, values);
        int count = groups.Count;
        if (take is int limit && limit < count)
        {
            var worstFirst = Comparer<int>.Create((a, b) => compare(b, a));
            var heap = new PriorityQueue<int, int>(limit + 1, worstFirst);
            for (int g = 0; g < count; g++)
            {
                if (heap.Count < limit)
                {
                    heap.Enqueue(g, g);
                }
                else if (limit > 0)
                {
                    heap.EnqueueDequeue(g, g);
                }
            }

            int[] kept = new int[heap.Count];
            for (int i = 0; i < kept.Length; i++)
            {
                kept[i] = heap.Dequeue();
            }

            Array.Sort(kept, compare);
            return kept;
        }

        int[] all = Enumerable.Range(0, count).ToArray();
        Array.Sort(all, compare);
        return all;
    }

    /// <summary>The query's order, then the dimensions' natural order, then first-seen order, so the answer is deterministic.</summary>
    private static Comparison<int> Comparison(Plan plan, Groups groups, double?[][] values)
    {
        GroupColumn[] dimensions = plan.Dimensions;
        int Natural(int a, int b)
        {
            for (int d = 0; d < dimensions.Length; d++)
            {
                int result = dimensions[d].Compare(groups.Code(d, a), groups.Code(d, b));
                if (result != 0)
                {
                    return result;
                }
            }

            return a.CompareTo(b);
        }

        if (plan.OrderColumn is not int column)
        {
            return Natural;
        }

        int sign = plan.Descending ? -1 : 1;
        if (column < dimensions.Length)
        {
            GroupColumn dimension = dimensions[column];
            return (a, b) =>
            {
                int codeA = groups.Code(column, a);
                int codeB = groups.Code(column, b);
                int result = codeA == 0 || codeB == 0
                    ? dimension.Compare(codeA, codeB)
                    : sign * dimension.Compare(codeA, codeB);
                return result != 0 ? result : Natural(a, b);
            };
        }

        int value = column - dimensions.Length;
        return (a, b) =>
        {
            double? x = values[a][value];
            double? y = values[b][value];
            int result = (x, y) switch
            {
                (null, null) => 0,
                (null, _) => 1,
                (_, null) => -1,
                _ => sign * x.Value.CompareTo(y.Value),
            };
            return result != 0 ? result : Natural(a, b);
        };
    }

    /// <summary>A query resolved against the dashboard: every name found, every rule checked (design §10.1).</summary>
    private sealed class Plan
    {
        private readonly Dictionary<object, int> numberSlots = [];
        private readonly Dictionary<object, int> distinctSlots = [];

        private Plan(GroupColumn[] dimensions, QueryColumn[] columns)
        {
            Dimensions = dimensions;
            Columns = columns;
        }

        public GroupColumn[] Dimensions { get; }

        public QueryColumn[] Columns { get; private set; }

        public Reader[] Values { get; private set; } = [];

        public List<NumberSource> Numbers { get; } = [];

        public List<DistinctSource> Distincts { get; } = [];

        public int? OrderColumn { get; private set; }

        public bool Descending { get; private set; }

        public static Plan Resolve<T>(Dashboard<T> dashboard, DashboardQuery<T> query)
        {
            IReadOnlyList<Dimension> groupBy = query.GroupBy ?? throw Error("The query's GroupBy is null.");
            IReadOnlyList<QueryValue> requested = query.Values ?? throw Error("The query's Values is null.");
            if (query.Selections is null)
            {
                throw Error("The query's Selections is null; use Selections.Empty for every row.");
            }

            if (groupBy.Count > 2)
            {
                throw Error($"A query groups by at most two dimensions; this one has {groupBy.Count}.");
            }

            if (query.Take is < 0)
            {
                throw Error($"Take must be zero or more; it is {query.Take}.");
            }

            var dimensions = new GroupColumn[groupBy.Count];
            var columns = new List<QueryColumn>();
            for (int d = 0; d < groupBy.Count; d++)
            {
                Dimension dimension = groupBy[d] ?? throw Error("The query's GroupBy holds a null dimension.");
                FacetIndex facet = Facet(dashboard, dimension.Name, "group by");
                GroupColumn column = facet.GroupColumn(dimension.Granularity);
                dimensions[d] = column;
                columns.Add(new QueryColumn(dimension.Key, facet.Name, QueryColumnRole.Dimension, column.Kind)
                {
                    Source = facet.Key,
                    Granularity = column.Granularity,
                });
            }

            var plan = new Plan(dimensions, []);
            var readers = new List<Reader>();
            IReadOnlyList<QueryValue> values = requested.Count == 0 ? [QueryValue.Count()] : requested;
            foreach (QueryValue value in values)
            {
                if (value is null)
                {
                    throw Error("The query's Values holds a null value.");
                }

                (Reader reader, QueryColumn column) = plan.ResolveValue(dashboard, value);
                readers.Add(reader);
                columns.Add(column);
            }

            string? duplicate = columns.GroupBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1)?.Key;
            if (duplicate is not null)
            {
                throw Error($"Two parts of the query have the key '{duplicate}'. Give one another key with As(...).");
            }

            plan.Columns = columns.ToArray();
            plan.Values = readers.ToArray();
            if (query.OrderBy is QueryOrder order)
            {
                int position = Array.FindIndex(plan.Columns, c => c.Key == order.Key);
                if (position < 0)
                {
                    throw Error($"The query orders by '{order.Key}', which is not one of its columns: {string.Join(", ", plan.Columns.Select(c => c.Key))}.");
                }

                plan.OrderColumn = position;
                plan.Descending = order.IsDescending;
            }

            return plan;
        }

        private (Reader Reader, QueryColumn Column) ResolveValue<T>(Dashboard<T> dashboard, QueryValue value)
        {
            if (value.MetricKey is string key)
            {
                MetricIndex metric = Metric(dashboard, key);
                return (MetricReader(dashboard, metric), new QueryColumn(value.Key, metric.Name, QueryColumnRole.Value, QueryColumnKind.Number)
                {
                    Source = metric.Key,
                    Aggregation = metric.Aggregation,
                });
            }

            Aggregation aggregation = value.Aggregation ?? throw Error($"The value '{value.Key}' names neither a metric nor an aggregation.");
            var column = new QueryColumn(value.Key, value.Key, QueryColumnRole.Value, QueryColumnKind.Number)
            {
                Source = value.Of,
                Aggregation = aggregation,
            };

            if (aggregation == Aggregation.Count)
            {
                return (new CountReader(), column);
            }

            string name = value.Of ?? throw Error($"The value '{value.Key}' has no name to aggregate.");
            FacetIndex facet = Facet(dashboard, name, aggregation.ToString());
            if (aggregation == Aggregation.Distinct)
            {
                DistinctSource keys = facet.DistinctKeys()
                    ?? throw Error($"Distinct needs values; facet '{name}' is a {facet.Kind} facet and has none.");
                return (new DistinctReader(DistinctSlot(keys)), column);
            }

            if (aggregation is Aggregation.Calculated)
            {
                throw Error($"A calculated value is a dashboard metric; ask for it with QueryValue.Metric(...).");
            }

            NumberSource numbers = facet.Numbers()
                ?? throw Error($"{aggregation} needs numbers, and facet '{name}' ({facet.Kind}) does not hold numbers.");
            return (new NumberReader(NumberSlot(numbers), aggregation), column);
        }

        /// <summary>How a metric is read per group. A calculated metric reads every metric defined before it, as in concept §4.4.</summary>
        private Reader MetricReader<T>(Dashboard<T> dashboard, MetricIndex metric)
        {
            if (metric.IsCalculated)
            {
                IReadOnlyList<MetricIndex> all = dashboard.MetricIndexes;
                int position = IndexOf(all, metric);
                var earlier = new Reader[position];
                for (int i = 0; i < position; i++)
                {
                    earlier[i] = MetricReader(dashboard, all[i]);
                }

                return new CalculatedReader(metric, all.Take(position).ToArray(), earlier);
            }

            if (metric.Distinct is DistinctColumn distinct)
            {
                return new DistinctReader(DistinctSlot(new CodeKeys(distinct, distinct.CodeAt)));
            }

            if (metric.Column is MetricColumn column)
            {
                return new NumberReader(NumberSlot(new ColumnNumbers(column, column.ValuesArray)), metric.Aggregation);
            }

            return new CountReader();
        }

        private static int IndexOf(IReadOnlyList<MetricIndex> all, MetricIndex metric)
        {
            for (int i = 0; i < all.Count; i++)
            {
                if (ReferenceEquals(all[i], metric))
                {
                    return i;
                }
            }

            throw new InvalidOperationException($"Metric '{metric.Key}' is not one of the dashboard's.");
        }

        /// <summary>One accumulation per column, however many values read it.</summary>
        private int NumberSlot(NumberSource source)
        {
            if (!numberSlots.TryGetValue(source.Identity, out int slot))
            {
                slot = Numbers.Count;
                Numbers.Add(source);
                numberSlots.Add(source.Identity, slot);
            }

            return slot;
        }

        private int DistinctSlot(DistinctSource source)
        {
            if (!distinctSlots.TryGetValue(source.Identity, out int slot))
            {
                slot = Distincts.Count;
                Distincts.Add(source);
                distinctSlots.Add(source.Identity, slot);
            }

            return slot;
        }

        private static FacetIndex Facet<T>(Dashboard<T> dashboard, string name, string use)
        {
            if (dashboard.TryGetFacetIndex(name, out FacetIndex facet))
            {
                return facet;
            }

            if (dashboard.TryGetMetricIndex(name, out _))
            {
                throw Error($"'{name}' is a metric, and a query cannot {use} a metric. Ask for its value with QueryValue.Metric(\"{name}\").");
            }

            IEnumerable<string> keys = dashboard.Facets.Select(f => f.Key);
            throw Error($"'{name}' is not a facet of this dashboard.{Suggest(name, keys)} Facets: {string.Join(", ", keys)}.");
        }

        private static MetricIndex Metric<T>(Dashboard<T> dashboard, string key)
        {
            if (dashboard.TryGetMetricIndex(key, out MetricIndex metric))
            {
                return metric;
            }

            IEnumerable<string> keys = dashboard.Metrics.Select(m => m.Key);
            string known = dashboard.Metrics.Count == 0 ? " The dashboard has no metrics." : $" Metrics: {string.Join(", ", keys)}.";
            throw Error($"'{key}' is not a metric of this dashboard.{Suggest(key, keys)}{known}");
        }

        /// <summary>The nearest known name, ignoring case and up to two edits away, so a caller or an agent can retry (design §10.5).</summary>
        private static string Suggest(string name, IEnumerable<string> candidates)
        {
            string? best = null;
            int bestDistance = 3;
            foreach (string candidate in candidates)
            {
                int distance = Distance(name.ToUpperInvariant(), candidate.ToUpperInvariant());
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            return best is null ? string.Empty : $" Did you mean '{best}'?";
        }

        private static int Distance(string a, string b)
        {
            var previous = new int[b.Length + 1];
            var current = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++)
            {
                previous[j] = j;
            }

            for (int i = 1; i <= a.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                    current[j] = Math.Min(substitution, Math.Min(previous[j], current[j - 1]) + 1);
                }

                (previous, current) = (current, previous);
            }

            return previous[b.Length];
        }

        private static ArgumentException Error(string message) => new(message, "query");
    }

    /// <summary>The groups the matching rows fall in, with their counts and accumulations (design §10.3, steps 4 and 5).</summary>
    private sealed class Groups
    {
        private readonly Plan plan;
        private readonly int stride;
        private readonly int[]? dense;
        private readonly Dictionary<long, int>? sparse;
        private int[] counts = new int[16];
        private int[][] codes;
        private MetricAccumulator[][] numbers;
        private readonly HashSet<(int Group, long Key)>[] distincts;
        private int[][]? distinctCounts;

        private Groups(Plan plan)
        {
            this.plan = plan;
            GroupColumn[] dimensions = plan.Dimensions;
            stride = dimensions.Length == 2 ? dimensions[1].CodeCount : 1;
            long combinations = dimensions.Length == 0 ? 1 : (long)dimensions[0].CodeCount * stride;
            if (combinations <= DenseGroupLimit)
            {
                dense = new int[combinations];
                Array.Fill(dense, -1);
            }
            else
            {
                sparse = [];
            }

            codes = dimensions.Select(_ => new int[16]).ToArray();
            numbers = plan.Numbers.Select(_ => new MetricAccumulator[16]).ToArray();
            distincts = plan.Distincts.Select(_ => new HashSet<(int Group, long Key)>()).ToArray();
        }

        public int Count { get; private set; }

        public int Code(int dimension, int group) => codes[dimension][group];

        public int RowCount(int group) => counts[group];

        public MetricAggregate Aggregate(int slot, int group) => numbers[slot][group].Result;

        public int DistinctCount(int slot, int group)
        {
            if (distinctCounts is null)
            {
                var all = new int[distincts.Length][];
                for (int s = 0; s < distincts.Length; s++)
                {
                    all[s] = new int[Count];
                    foreach ((int g, long _) in distincts[s])
                    {
                        all[s][g]++;
                    }
                }

                distinctCounts = all;
            }

            return distinctCounts[slot][group];
        }

        public static Groups Collect(Plan plan, RowSet rows, CancellationToken cancellationToken)
        {
            var groups = new Groups(plan);
            GroupColumn[] dimensions = plan.Dimensions;
            if (dimensions.Length == 0)
            {
                // No dimensions: one group over every row asked about, present even when there are none (concept §4.11).
                int all = groups.GroupOf(0, 0);
                int seen = 0;
                foreach (int row in rows)
                {
                    if (++seen % CancellationStride == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    groups.Add(row, all);
                }

                return groups;
            }

            GroupColumn first = dimensions[0];
            GroupColumn? second = dimensions.Length == 2 ? dimensions[1] : null;
            int[] firstBuffer = new int[1];
            int[] secondBuffer = new int[1];
            int visited = 0;
            foreach (int row in rows)
            {
                if (++visited % CancellationStride == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                ReadOnlySpan<int> firstCodes = CodesOf(first, row, firstBuffer);
                ReadOnlySpan<int> secondCodes = second is null ? NullCode : CodesOf(second, row, secondBuffer);
                foreach (int a in firstCodes)
                {
                    foreach (int b in secondCodes)
                    {
                        groups.Add(row, groups.GroupOf(a, b));
                    }
                }
            }

            return groups;
        }

        /// <summary>The row's codes in one dimension; a multi-valued row with no items is the null value (concept §5).</summary>
        private static ReadOnlySpan<int> CodesOf(GroupColumn dimension, int row, int[] buffer)
        {
            if (dimension.IsMultiValued)
            {
                ReadOnlySpan<int> codes = dimension.CodesAt(row);
                return codes.IsEmpty ? NullCode : codes;
            }

            buffer[0] = dimension.CodeAt(row);
            return buffer;
        }

        private int GroupOf(int first, int second)
        {
            long key = (long)first * stride + second;
            if (dense is not null)
            {
                int existing = dense[key];
                if (existing >= 0)
                {
                    return existing;
                }

                int created = Create(first, second);
                dense[key] = created;
                return created;
            }

            if (!sparse!.TryGetValue(key, out int group))
            {
                group = Create(first, second);
                sparse.Add(key, group);
            }

            return group;
        }

        private int Create(int first, int second)
        {
            int group = Count++;
            if (group == counts.Length)
            {
                int capacity = counts.Length * 2;
                Array.Resize(ref counts, capacity);
                for (int d = 0; d < codes.Length; d++)
                {
                    Array.Resize(ref codes[d], capacity);
                }

                for (int s = 0; s < numbers.Length; s++)
                {
                    Array.Resize(ref numbers[s], capacity);
                }
            }

            if (codes.Length > 0)
            {
                codes[0][group] = first;
            }

            if (codes.Length > 1)
            {
                codes[1][group] = second;
            }

            return group;
        }

        private void Add(int row, int group)
        {
            counts[group]++;
            for (int s = 0; s < numbers.Length; s++)
            {
                double value = plan.Numbers[s].ValueAt(row);
                if (!double.IsNaN(value))
                {
                    numbers[s][group].Add(value);
                }
            }

            for (int s = 0; s < distincts.Length; s++)
            {
                plan.Distincts[s].AddTo(row, group, distincts[s]);
            }
        }
    }

    /// <summary>How one value of the answer is read from a group, with "no value" as null (concept §4.4).</summary>
    private abstract class Reader
    {
        public abstract double? Read(Groups groups, int group);
    }

    private sealed class CountReader : Reader
    {
        public override double? Read(Groups groups, int group) => groups.RowCount(group);
    }

    private sealed class NumberReader(int slot, Aggregation aggregation) : Reader
    {
        public override double? Read(Groups groups, int group)
        {
            MetricAggregate aggregate = groups.Aggregate(slot, group);
            return aggregation switch
            {
                Aggregation.Sum => aggregate.SumOrNull,
                Aggregation.Average => aggregate.Average,
                Aggregation.Min => aggregate.MinOrNull,
                Aggregation.Max => aggregate.MaxOrNull,
                _ => throw new InvalidOperationException($"Unexpected aggregation {aggregation}."),
            };
        }
    }

    private sealed class DistinctReader(int slot) : Reader
    {
        public override double? Read(Groups groups, int group) =>
            groups.DistinctCount(slot, group) is int count and > 0 ? count : null;
    }

    /// <summary>A calculated metric per group: its formula over the group's values of the metrics defined before it. No shares, since a group is not a part of anything.</summary>
    private sealed class CalculatedReader(MetricIndex metric, MetricIndex[] earlier, Reader[] readers) : Reader
    {
        public override double? Read(Groups groups, int group)
        {
            var states = new MetricState[earlier.Length];
            for (int i = 0; i < states.Length; i++)
            {
                states[i] = new MetricState(earlier[i].Key, earlier[i].Name, earlier[i].Aggregation, readers[i].Read(groups, group), null);
            }

            return metric.Evaluate(new MetricValues(states, states.Length));
        }
    }
}
