using Linq2Dashboard.SampleData;

namespace Linq2Dashboard.Sample.Components.Pages;

/// <summary>
/// A small dataset per page (concept §7): the view builds its own dashboard over one country's rows, with the facets
/// defined in the markup, and builds again when the country changes. Each country's rows are one array, kept so the
/// view sees the same reference until the country changes (design §9).
/// </summary>
public partial class Small
{
    private static readonly Dictionary<string, SampleOrder[]> RowsByCountry = SampleOrders.Generate(20_000)
        .GroupBy(o => o.Country)
        .ToDictionary(g => g.Key, g => g.ToArray());

    private static readonly string[] Countries = [.. RowsByCountry.Keys.Order(StringComparer.Ordinal)];

    private static readonly double[] AmountCuts = [50, 100, 250, 500, 1000];

    private static readonly DatePreset[] DatePresets = [DatePreset.Last30Days, DatePreset.ThisYear, DatePreset.LastYear];

    private string country = Countries[0];

    private SampleOrder[] Rows => RowsByCountry[country];

    /// <summary>The text facet's match function: a customer name or a channel containing the text.</summary>
    private static bool MatchesCustomer(SampleOrder order, string text) =>
        (order.Customer?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false) || order.Channel.Contains(text, StringComparison.OrdinalIgnoreCase);
}
