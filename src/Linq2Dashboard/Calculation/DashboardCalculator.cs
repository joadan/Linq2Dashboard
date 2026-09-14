using Linq2Dashboard.Facets;
using Linq2Dashboard.Indexing;
using Linq2Dashboard.Metrics;

namespace Linq2Dashboard.Calculation;

/// <summary>The calculation pipeline of design §4: selection row sets, matching set, per-facet contexts, counting, metrics.</summary>
internal static class DashboardCalculator
{
    public static DashboardState<T> Calculate<T>(Dashboard<T> dashboard, Selections selections)
    {
        IReadOnlyList<FacetIndex> facets = dashboard.FacetIndexes;
        int facetCount = facets.Count;

        // §4.1 one row set per facet with a selection (cached by the dashboard)
        var rowSets = new RowSet?[facetCount];
        var selectionOf = new Selection?[facetCount];
        foreach ((string key, Selection selection) in selections)
        {
            if (!dashboard.FacetPositions.TryGetValue(key, out int position))
            {
                throw new ArgumentException($"Unknown facet key '{key}'.", nameof(selections));
            }

            selectionOf[position] = selection;
            rowSets[position] = dashboard.RowsMatching(facets[position], selection);
        }

        // §4.2 matching set and contexts via prefix/suffix products over the active facets
        int[] active = Enumerable.Range(0, facetCount).Where(p => rowSets[p] is not null).ToArray();
        int k = active.Length;
        RowSet all = dashboard.All;

        var prefix = new RowSet[k + 1];
        prefix[0] = all;
        for (int i = 0; i < k; i++)
        {
            prefix[i + 1] = prefix[i].And(rowSets[active[i]]!);
        }

        var suffix = new RowSet[k + 1];
        suffix[k] = all;
        for (int i = k - 1; i >= 0; i--)
        {
            suffix[i] = rowSets[active[i]]!.And(suffix[i + 1]);
        }

        RowSet matching = prefix[k];

        var contexts = new RowSet[facetCount];
        int activeIndex = 0;
        for (int p = 0; p < facetCount; p++)
        {
            if (rowSets[p] is null)
            {
                contexts[p] = matching;
            }
            else
            {
                contexts[p] = prefix[activeIndex].And(suffix[activeIndex + 1]);
                activeIndex++;
            }
        }

        // §4.3–4.5 present every facet against its own context
        var facetStates = new FacetState[facetCount];
        if (dashboard.ParallelCounting && facetCount > 1)
        {
            Parallel.For(0, facetCount, p => facetStates[p] = facets[p].Present(contexts[p], selectionOf[p]));
        }
        else
        {
            for (int p = 0; p < facetCount; p++)
            {
                facetStates[p] = facets[p].Present(contexts[p], selectionOf[p]);
            }
        }

        // §4.6 metrics over the matching set
        IReadOnlyList<MetricIndex> metrics = dashboard.MetricIndexes;
        var metricStates = new MetricState[metrics.Count];
        for (int i = 0; i < metricStates.Length; i++)
        {
            metricStates[i] = metrics[i].Present(matching);
        }

        return new DashboardState<T>(dashboard, selections, matching, facetStates, metricStates);
    }
}
