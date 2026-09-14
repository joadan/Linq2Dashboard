using System.Linq.Expressions;
using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Facets;

/// <summary>
/// A facet as configured by the builder, before the dataset is known. Mutable while the
/// <see cref="DashboardBuilder{T}"/> is being configured, frozen at build. Turns into a
/// <see cref="FacetIndex"/> once the rows are available (design §6).
/// </summary>
internal abstract class FacetDefinition<T>
{
    protected FacetDefinition(string key, FacetKind kind)
    {
        Key = key;
        Kind = kind;
        Title = key;
    }

    public string Key { get; }

    public string Title { get; set; }

    public FacetKind Kind { get; }

    public FacetInfo Info => new(Key, Title, Kind);

    /// <summary>
    /// Builds the index once, at <c>Create</c>. <paramref name="parallel"/> is the dashboard's
    /// parallel-counting option, which the text facet's scan follows (design §4.1).
    /// </summary>
    public abstract FacetIndex Build(T[] items, TimeProvider timeProvider, bool parallel);
}

/// <summary>Derives a facet or metric key from a selector expression (concept §7).</summary>
internal static class SelectorKey
{
    /// <summary>
    /// The member name at the end of a member-access chain such as <c>x => x.Address.Country</c>.
    /// Fails for anything else (method calls, arithmetic, constants), which then needs an explicit key.
    /// </summary>
    public static bool TryDerive(LambdaExpression selector, out string key)
    {
        Expression body = selector.Body;
        while (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
        {
            body = unary.Operand;
        }

        if (body is MemberExpression member)
        {
            key = member.Member.Name;
            return true;
        }

        key = string.Empty;
        return false;
    }

    public static string Derive(LambdaExpression selector, string paramName)
    {
        if (!TryDerive(selector, out string key))
        {
            throw new ArgumentException(
                $"Cannot derive a key from '{selector}'. Only member access selectors get a key automatically; supply an explicit key.",
                paramName);
        }

        return key;
    }
}
