using System.Linq.Expressions;
using Linq2Dashboard.Facets;

namespace Linq2Dashboard;

/// <summary>
/// The key a facet gets from its selector (concept §7): the member name at the end of a member-access
/// chain, the same rule the builder applies. Lets code and markup name a facet by the selector instead
/// of retyping the string: <c>FacetKey.Of&lt;Order&gt;(x => x.Country)</c> is <c>"Country"</c>.
/// </summary>
public static class FacetKey
{
    /// <summary>
    /// The key the builder derives from <paramref name="selector"/>. A value-type member arrives boxed,
    /// <c>x => (object)x.Amount</c>, and the conversion is looked through. Throws for anything that is not a
    /// member access, since such a facet only has an explicit key.
    /// </summary>
    public static string Of<T>(Expression<Func<T, object?>> selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        return SelectorKey.Derive(selector, nameof(selector));
    }
}
