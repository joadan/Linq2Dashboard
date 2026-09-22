using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// Base for the facet components that render a facet declared from a selector: <see cref="ValueFacet{T}"/>,
/// <see cref="RangeFacet{T}"/> and <see cref="DateFacet{T}"/>. The facet is named either by its <see cref="Key"/>
/// or by the selector it was declared from, <see cref="For"/>, which derives the same key the builder did
/// (concept §7, design §9), so the member name is checked by the compiler and follows a rename.
/// </summary>
public abstract class FacetComponentBase<T> : DashboardComponentBase<T>
{
    /// <summary>The facet key, as defined in the builder. Exactly one of <see cref="Key"/> and <see cref="For"/> is set.</summary>
    [Parameter]
    public string? Key { get; set; }

    /// <summary>
    /// The selector the facet was declared from in the builder, <c>For="x => x.Country"</c>, for a facet whose key was derived
    /// from a member. The key is derived here by the same rule (concept §7). Exactly one of <see cref="Key"/> and <see cref="For"/> is set.
    /// </summary>
    [Parameter]
    public Expression<Func<T, object?>>? For { get; set; }

    /// <summary>The key the component renders: <see cref="Key"/>, or the key derived from <see cref="For"/>.</summary>
    protected string ResolvedKey { get; private set; } = string.Empty;

    /// <summary>Resolves the key from <see cref="Key"/> or <see cref="For"/>; throws when both or neither are set.</summary>
    protected override void OnParametersSet()
    {
        if (For is not null && Key is not null)
        {
            throw new InvalidOperationException($"{ComponentName} takes either Key or For, not both: Key is '{Key}' and For is '{For}'.");
        }

        if (For is null && string.IsNullOrEmpty(Key))
        {
            throw new InvalidOperationException($"{ComponentName} needs a Key, or a For selector for a facet declared from a member.");
        }

        ResolvedKey = For is null ? Key! : FacetKey.Of(For);
    }
}
