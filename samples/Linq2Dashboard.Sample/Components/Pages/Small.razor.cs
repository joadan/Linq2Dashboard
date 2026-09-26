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

    private string country = Countries[0];

    private SampleOrder[] Rows => RowsByCountry[country];
}
