using System.Linq.Expressions;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// Base for the facet components that render a facet declared from a selector: <see cref="ValueFacet{T}"/>,
/// <see cref="RangeFacet{T}"/> and <see cref="DateFacet{T}"/>. The facet is named either by its <see cref="Key"/>
/// or by the selector it was declared from, <see cref="For"/>, which derives the same key the builder did
/// (concept §7, design §9), so the member name is checked by the compiler and follows a rename. Under a view with
/// <c>Items</c> the selector also defines the facet when nothing else does (design §9).
/// </summary>
public abstract class FacetComponentBase<T> : DashboardComponentBase<T>
{
    /// <summary>
    /// The facet key, as defined in the builder. Exactly one of <see cref="Key"/> and <see cref="For"/> is set, except under a
    /// view with <c>Items</c>, where both together define a facet whose selector has no member name to derive a key from.
    /// </summary>
    [Parameter]
    public string? Key { get; set; }

    /// <summary>
    /// The selector the facet was declared from in the builder, <c>For="x => x.Country"</c>, for a facet whose key was derived
    /// from a member. The key is derived here by the same rule (concept §7). Under a view with <c>Items</c>, the selector also
    /// defines the facet when neither <c>Build</c> nor another component does (design §9).
    /// </summary>
    [Parameter]
    public Expression<Func<T, object?>>? For { get; set; }

    /// <summary>The key the component renders: <see cref="Key"/>, or the key derived from <see cref="For"/>.</summary>
    protected string ResolvedKey { get; private set; } = string.Empty;

    /// <summary>True while the view has yet to define this component's facet; the component renders nothing meanwhile (design §9).</summary>
    private protected bool Pending => AwaitingDefinition(isMetric: false, ResolvedKey);

    /// <summary>True when any parameter that only a definition uses is set, so the component asks to define its facet.</summary>
    private protected virtual bool HasDefinitionParameters => false;

    /// <summary>The facet this component defines from <paramref name="selector"/>, the <see cref="For"/> selector without its boxing; null for a component that only displays.</summary>
    private protected virtual MarkupDefinition<T>? Definition(string key, LambdaExpression selector) => null;

    /// <summary>Resolves the key from <see cref="Key"/> or <see cref="For"/>, then registers the definition with the view.</summary>
    protected override void OnParametersSet()
    {
        bool definesWithKey = For is not null && Key is not null && Context.Registry?.BuildsOwnDashboard == true;
        if (For is not null && Key is not null && !definesWithKey)
        {
            throw new InvalidOperationException($"{ComponentName} takes either Key or For, not both: Key is '{Key}' and For is '{For}'.");
        }

        if (For is null && string.IsNullOrEmpty(Key))
        {
            throw new InvalidOperationException($"{ComponentName} needs a Key, or a For selector for a facet declared from a member.");
        }

        ResolvedKey = Key ?? FacetKey.Of(For!);
        if (For is not null)
        {
            if (Definition(ResolvedKey, MarkupFacets.Unbox(For)) is { } definition)
            {
                Register(definition);
            }
        }
        else if (HasDefinitionParameters)
        {
            throw new InvalidOperationException(
                $"{ComponentName} '{ResolvedKey}' has definition parameters but no For: a facet is defined from its selector, so give it For.");
        }
    }
}
