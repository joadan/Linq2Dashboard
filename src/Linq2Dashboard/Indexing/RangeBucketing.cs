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
    /// About <paramref name="count"/> buckets of equal width on round boundaries over the body of the
    /// distribution, with an open bucket at each end for the values outside it (design §3.3).
    /// A dataset with no values gets no buckets; one where every value is equal gets one.
    /// </summary>
    public static RangeBucketing Auto(int count) => new AutoBucketing(count);

    /// <summary>
    /// Resolves to edges. <paramref name="min"/> and <paramref name="max"/> are NaN when the dataset has
    /// no values. <paramref name="values"/> is the column, NaN where null.
    /// </summary>
    public abstract double[] Edges(double min, double max, ReadOnlySpan<double> values);

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

        public override double[] Edges(double min, double max, ReadOnlySpan<double> values)
        {
            var edges = new double[cuts.Length + 2];
            edges[0] = double.NegativeInfinity;
            cuts.CopyTo(edges, 1);
            edges[^1] = double.PositiveInfinity;
            return edges;
        }
    }

    /// <summary>
    /// Round boundaries over the body of the data (design §3.3). The body is the 2nd to 98th
    /// percentile, read from a stride sample of at most <see cref="MaxSample"/> values so a large
    /// column costs one small sort. The step is the value from the 1, 2, 5 series times a power of
    /// ten nearest to <c>body / count</c>, the edges are the multiples of the step that cover the
    /// body, and an infinite edge is added at an end only when some value lies beyond it.
    /// </summary>
    private sealed record AutoBucketing : RangeBucketing
    {
        private const int MaxSample = 100_000;
        private const double LowPercentile = 0.02;
        private const double HighPercentile = 0.98;

        private readonly int count;

        public AutoBucketing(int count)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
            this.count = count;
        }

        public override double[] Edges(double min, double max, ReadOnlySpan<double> values)
        {
            if (double.IsNaN(min) || double.IsNaN(max))
            {
                return [];
            }

            if (min == max)
            {
                return [min, max];
            }

            (double low, double high) = Body(values);
            if (!(low < high))
            {
                // Heavy ties or a degenerate sample: fall back to the whole range.
                low = min;
                high = max;
            }

            double step = NiceStep((high - low) / count);
            int decimals = DecimalsOf(step);
            double first = Math.Floor(low / step) * step;
            double last = Math.Ceiling(high / step) * step;
            int inner = (int)Math.Round((last - first) / step) + 1;

            var edges = new List<double>(inner + 2);
            if (min < first)
            {
                edges.Add(double.NegativeInfinity);
            }

            for (int i = 0; i < inner; i++)
            {
                edges.Add(Round(first + step * i, decimals));
            }

            if (max > edges[^1])
            {
                edges.Add(double.PositiveInfinity);
            }

            return edges.ToArray();
        }

        /// <summary>The 2nd and 98th percentile of the non-null values, from an evenly strided sample.</summary>
        private static (double Low, double High) Body(ReadOnlySpan<double> values)
        {
            int nonNull = 0;
            foreach (double value in values)
            {
                if (!double.IsNaN(value))
                {
                    nonNull++;
                }
            }

            int stride = Math.Max(1, (nonNull + MaxSample - 1) / MaxSample);
            var sample = new double[(nonNull + stride - 1) / stride];
            int seen = 0;
            int taken = 0;
            foreach (double value in values)
            {
                if (double.IsNaN(value))
                {
                    continue;
                }

                if (seen % stride == 0)
                {
                    sample[taken++] = value;
                }

                seen++;
            }

            Array.Sort(sample, 0, taken);
            int lowIndex = (int)Math.Floor(LowPercentile * (taken - 1));
            int highIndex = (int)Math.Ceiling(HighPercentile * (taken - 1));
            return (sample[lowIndex], sample[highIndex]);
        }

        /// <summary>The 1, 2 or 5 times a power of ten nearest to <paramref name="raw"/> on a log scale.</summary>
        private static double NiceStep(double raw)
        {
            double power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double mantissa = raw / power;
            double factor = mantissa >= Math.Sqrt(50) ? 10 : mantissa >= Math.Sqrt(10) ? 5 : mantissa >= Math.Sqrt(2) ? 2 : 1;
            return factor * power;
        }

        /// <summary>Decimal places needed to write <paramref name="step"/> exactly, or -1 when there are too many to round to.</summary>
        private static int DecimalsOf(double step)
        {
            int exponent = (int)Math.Floor(Math.Log10(step));
            int decimals = exponent < 0 ? -exponent : 0;
            return decimals <= 15 ? decimals : -1;
        }

        /// <summary>Removes the binary noise from a multiple of the step, so an edge reads 0.6 rather than 0.6000000000000001.</summary>
        private static double Round(double edge, int decimals) =>
            decimals < 0 ? edge : Math.Round(edge, decimals);
    }
}
