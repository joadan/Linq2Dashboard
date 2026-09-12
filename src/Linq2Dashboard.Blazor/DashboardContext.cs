namespace Linq2Dashboard.Blazor;

/// <summary>
/// What every component inside a <see cref="DashboardView{T}"/> shares: the dashboard, the current
/// selections and state, and the formatter (design §9). Owns the click loop from concept §2:
/// change the selections, calculate, tell the components. Components hold no state of their own.
/// </summary>
public sealed class DashboardContext<T>
{
    private readonly Func<Selections, Task> onSelectionsChanged;

    internal DashboardContext(Dashboard<T> dashboard, Selections selections, IDashboardFormatter formatter, Func<Selections, Task> onSelectionsChanged)
    {
        Dashboard = dashboard;
        Formatter = formatter;
        Selections = selections;
        State = dashboard.Calculate(selections);
        this.onSelectionsChanged = onSelectionsChanged;
    }

    public Dashboard<T> Dashboard { get; }

    public IDashboardFormatter Formatter { get; }

    /// <summary>The current selections. Immutable; every change produces a new instance.</summary>
    public Selections Selections { get; private set; }

    /// <summary>The state for <see cref="Selections"/>. Always consistent with it.</summary>
    public DashboardState<T> State { get; private set; }

    /// <summary>Raised after <see cref="State"/> changed. Components re-render on it.</summary>
    public event Action? StateChanged;

    /// <summary>Replaces the selections, recalculates, notifies components and the host.</summary>
    public Task ApplyAsync(Selections selections)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (selections.Equals(Selections))
        {
            return Task.CompletedTask;
        }

        Recalculate(selections);
        return onSelectionsChanged(selections);
    }

    /// <summary>The click on a value facet: add the value if absent, remove it if present.</summary>
    public Task ToggleAsync(string key, object? value) => ApplyAsync(Selections.Toggle(key, value));

    /// <summary>The click on a bucket or preset, or a slider change: replace the facet's selection.</summary>
    public Task SelectAsync(string key, Selection selection) => ApplyAsync(Selections.With(key, selection));

    public Task ClearAsync(string key) => ApplyAsync(Selections.Clear(key));

    public Task ClearAllAsync() => ApplyAsync(Selections.Empty);

    /// <summary>Adopts selections set by the host through the component parameter, without echoing them back.</summary>
    internal void Sync(Selections selections)
    {
        if (!selections.Equals(Selections))
        {
            Recalculate(selections);
        }
    }

    private void Recalculate(Selections selections)
    {
        Selections = selections;
        State = Dashboard.Calculate(selections);
        StateChanged?.Invoke();
    }
}
