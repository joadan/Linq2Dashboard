using System.Linq.Expressions;

namespace Linq2Dashboard.AI;

/// <summary>
/// How <see cref="DashboardToolsExtensions.ToTools{T}(Dashboard{T}, DashboardToolOptions{T})"/> offers a dashboard to an
/// agent (design §10.10): the tools' names and texts, the names the agent may use, what they mean, the where and showing.
/// </summary>
/// <typeparam name="T">The dashboard's row type.</typeparam>
public sealed class DashboardToolOptions<T>
{
    /// <summary>
    /// The prefix of every tool name, such as <c>orders</c> for <c>orders_describe</c>, <c>orders_query</c> and
    /// <c>orders_show</c>. Letters, digits, <c>_</c> and <c>-</c>, at most 40 characters. An application with several
    /// dashboards gives each its own name and its own tools.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>What the dashboard holds, in a sentence, such as "Customer orders since 2020, one row per order." It opens every tool's description, so the agent can pick the right dashboard.</summary>
    public string? Summary { get; init; }

    /// <summary>
    /// The facet keys, metric keys and property names the agent may use, in describe, query and show alike. Null allows
    /// every name the dashboard knows. It limits names, not rows: a user who may see only some rows gets tools over a
    /// scope of their own (concept §4.10).
    /// </summary>
    public IReadOnlyCollection<string>? Allowed { get; init; }

    /// <summary>
    /// The application's text per facet key, metric key or property name: what a value means, its unit, how it differs
    /// from a similar one. Give one for every id with a label above all (design §10.9). Passed to
    /// <see cref="DescribeOptions.Descriptions"/>, so a name the dashboard does not know is an error.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Descriptions { get; init; }

    /// <summary>A facet with at most this many values lists them in describe; above it only the count is given. 25 by default.</summary>
    public int ValueLimit { get; init; } = 25;

    /// <summary>
    /// The properties the where may filter on, in Gridify syntax. Null allows every property in <see cref="Allowed"/>
    /// (or every property, without it) of a type a filter can compare: text, numbers, booleans, enums, <see cref="Guid"/>
    /// and dates. Empty leaves the where out of the query tool. Ignored when <see cref="Where"/> is given.
    /// </summary>
    public IReadOnlyCollection<string>? Filterable { get; init; }

    /// <summary>The application's own parser for the where text, used instead of the Gridify mapper the tools build.</summary>
    public Func<string, Expression<Func<T, bool>>>? Where { get; init; }

    /// <summary>What <see cref="Where"/> accepts, such as its fields and an example; shown in describe and in the where's errors.</summary>
    public string? WhereHelp { get; init; }

    /// <summary>
    /// Puts selections on the user's screen, such as <c>s =&gt; InvokeAsync(() =&gt; dash.ApplyAsync(s))</c> on a Blazor
    /// page. Without it there is no show tool. The selections have been read strictly and checked against
    /// <see cref="Allowed"/> before it is called.
    /// </summary>
    public Func<Selections, Task>? Show { get; init; }
}
