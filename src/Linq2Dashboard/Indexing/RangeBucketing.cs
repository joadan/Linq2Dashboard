namespace Linq2Dashboard.Indexing;

/// <summary>
/// How a range facet divides its values into buckets. Resolved once at build into a sorted array
/// of edges; bucket <c>i</c> covers <c>[edges[i], edges[i+1])</c>, and the last bucket also includes
/// its upper edge. Edges may be infinite (design §3.3, concept §5).
/// </summary>
internal abstract record RangeBucketing
{
    /// <summary>
    /// Cut points supplied by the application. <c>Explicit(100, 500, 1000)</c> gives four buckets:
    /// below 100, 100 to 500, 500 to 1000, and 1000 and above. Every value lands somewhere.
    /// </summary>
    public static RangeBucketing Explicit(params double[] cuts) => new ExplicitBucketing(cuts);

    /// <summary>
    /// <paramref name="count"/> equal-width buckets between the dataset's minimum and maximum.
    /// A dataset with no values gets no buckets; one where every value is equal gets one.
    /// </summary>
    public static RangeBucketing Auto(int count) => new AutoBucketing(count);

    /// <summary>Resolves to edges. <paramref name="min"/> and <paramref name="max"/> are NaN when the dataset has no values.</summary>
    public abstract double[] Edges(double min, double max);

    private sealed record ExplicitBucketing : RangeBucketing
    {
        private readonly double[] cuts;

        public ExplicitBucketing(double[] cuts)
        {
            ArgumentNullException.ThrowIfNull(cuts);
            for (int i = 0; i < cuts.Length; i++)
            {
                if (double.IsNaN(cuts[i]) || double.IsInfinity(cuts[i]))
                {
                    throw new ArgumentException("Cut points must be finite numbers.", nameof(cuts));
                }

                if (i > 0 && cuts[i] <= cuts[i - 1])
                {
                    throw new ArgumentException("Cut points must be strictly ascending.", nameof(cuts));
                }
            }

            this.cuts = (double[])cuts.Clone();
        }

        public override double[] Edges(double min, double max)
        {
            var edges = new double[cuts.Length + 2];
            edges[0] = double.NegativeInfinity;
            cuts.CopyTo(edges, 1);
            edges[^1] = double.PositiveInfinity;
            return edges;
        }
    }

    private sealed record AutoBucketing : RangeBucketing
    {
        private readonly int count;

        public AutoBucketing(int count)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
            this.count = count;
        }

        public override double[] Edges(double min, double max)
        {
            if (double.IsNaN(min) || double.IsNaN(max))
            {
                return [];
            }

            if (min == max)
            {
                return [min, max];
            }

            var edges = new double[count + 1];
            double width = (max - min) / count;
            for (int i = 0; i < count; i++)
            {
                edges[i] = min + width * i;
            }

            edges[count] = max;
            return edges;
        }
    }
}
