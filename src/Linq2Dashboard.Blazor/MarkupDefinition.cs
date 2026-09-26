namespace Linq2Dashboard.Blazor;

/// <summary>
/// A facet or metric that a component inside an <c>Items</c> view defines from its parameters (concept §7, design §9).
/// The view collects them, orders them and runs them on its builder after <see cref="DashboardView{T}.Build"/>.
/// </summary>
/// <param name="Owner">The component that defines it; the same component registering again updates its definition.</param>
/// <param name="Component">The component's name, for messages.</param>
/// <param name="IsMetric">A metric rather than a facet: the two have separate key spaces.</param>
/// <param name="Key">The facet or metric key.</param>
/// <param name="Explicit">True when the component carries definition parameters; false when it only names a selector, which defines with defaults unless something else defines the key.</param>
/// <param name="Watched">The comparable definition values: a change rebuilds the dashboard (values are watched, code is not).</param>
/// <param name="Apply">Adds the facet or metric to the builder.</param>
/// <param name="IsCalculated">A calculated metric, which the view defines after every plain metric.</param>
internal sealed record MarkupDefinition<T>(
    object Owner,
    string Component,
    bool IsMetric,
    string Key,
    bool Explicit,
    IReadOnlyList<object?> Watched,
    Action<DashboardBuilder<T>> Apply,
    bool IsCalculated = false)
{
    /// <summary>"facet 'Country'" or "metric 'revenue'", for messages.</summary>
    public string Describe() => $"{(IsMetric ? "metric" : "facet")} '{Key}'";

    /// <summary>Whether the watched values equal <paramref name="other"/>'s; arrays compare by content.</summary>
    public bool SameValues(MarkupDefinition<T> other) =>
        Watched.Count == other.Watched.Count && Watched.Zip(other.Watched).All(pair => ValueEquals(pair.First, pair.Second));

    private static bool ValueEquals(object? a, object? b) =>
        a is System.Collections.IEnumerable left and not string && b is System.Collections.IEnumerable right and not string
            ? left.Cast<object?>().SequenceEqual(right.Cast<object?>())
            : Equals(a, b);
}

/// <summary>What the components inside a view ask of it about markup definitions. Implemented by <see cref="DashboardView{T}"/>.</summary>
internal interface IMarkupRegistry<T>
{
    /// <summary>True when the view builds its own dashboard from <c>Items</c>, so components may define.</summary>
    bool BuildsOwnDashboard { get; }

    /// <summary>True when every definition registered so far is in the current dashboard.</summary>
    bool Settled { get; }

    /// <summary>Registers or updates a definition; throws when it clashes with another or the view renders a prebuilt dashboard.</summary>
    void Define(MarkupDefinition<T> definition);

    /// <summary>
    /// Asks the view to settle before a component names a key the dashboard lacks, since a component later in the
    /// same pass may define it. Returns true when the key was already asked for, so a still-unknown key is an error.
    /// </summary>
    bool AskFor(bool isMetric, string key);
}
