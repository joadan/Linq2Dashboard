namespace Linq2Dashboard.SampleData;

/// <summary>
/// The keys the sample dashboard gives its explicitly keyed facets and its metrics, so the builder and every page
/// spell each one once. Facets declared from a member need no constant: the components take the same selector
/// through <c>For</c> (concept §7).
/// </summary>
public static class SampleOrderKeys
{
    /// <summary>The customer facet: counted by id, shown by name.</summary>
    public const string Customer = "Customer";

    /// <summary>The free-text facet over customer and channel.</summary>
    public const string Search = "search";

    /// <summary>Matching orders.</summary>
    public const string Orders = "orders";

    /// <summary>Sum of the amounts.</summary>
    public const string Revenue = "revenue";

    /// <summary>Average order amount.</summary>
    public const string Average = "average";

    /// <summary>Distinct customers.</summary>
    public const string Customers = "customers";

    /// <summary>Revenue divided by customers.</summary>
    public const string PerCustomer = "perCustomer";
}
