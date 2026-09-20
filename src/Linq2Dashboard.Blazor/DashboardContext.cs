namespace Linq2Dashboard.Blazor;

/// <summary>
/// What every component inside a <see cref="DashboardView{T}"/> shares: the dashboard, the current
/// selections and state, and the formatter (design §9). Owns the click loop from concept §2:
/// change the selections, calculate, tell the components. Components hold no state of their own.
/// </summary>
public sealed class DashboardContext<T>
{
    private readonly Func<Selections, Task> onSelectionsChanged;
    private readonly Func<DashboardState<T>, Task> onStateChanged;
    private IQueryable<T>? items;
    private Func<Selections, string>? hrefFor;

    internal DashboardContext(
        Dashboard<T> dashboard,
        Selections selections,
        IDashboardFormatter formatter,
        Func<Selections, Task> onSelectionsChanged,
        Func<DashboardState<T>, Task> onStateChanged)
    {
        Dashboard = dashboard;
        Formatter = formatter;
        Selections = selections;
        State = TimedCalculate(selections);
        this.onSelectionsChanged = onSelectionsChanged;
        this.onStateChanged = onStateChanged;
    }

    /// <summary>The dashboard being rendered. Changes when the host gives the view another dashboard, such as a scope of the first (concept §4.10); the context itself stays.</summary>
    public Dashboard<T> Dashboard { get; private set; }

    /// <summary>The formatter every component inside uses.</summary>
    public IDashboardFormatter Formatter { get; private set; }

    /// <summary>The current selections. Immutable; every change produces a new instance.</summary>
    public Selections Selections { get; private set; }

    /// <summary>The state for <see cref="Selections"/>. Always consistent with it.</summary>
    public DashboardState<T> State { get; private set; }

    /// <summary>
    /// <see cref="DashboardState{T}.Items"/> as one <see cref="IQueryable{T}"/> per state, for a data grid's items
    /// parameter (design §9). The reference changes exactly when the state does, so a grid that re-queries when its
    /// source changes, QuickGrid among them, does so once per calculation and not on every render of the page. The
    /// list behind it is counted and indexable, so the grid's count, page, viewport and sort stay cheap (design §4.7).
    /// </summary>
    public IQueryable<T> Items => items ??= State.Items.AsQueryable();

    /// <summary>Raised after <see cref="State"/> changed. Components re-render on it; the host listens through <see cref="DashboardView{T}.StateChanged"/>.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// True when clicks render as links instead of buttons: the view is not interactive (static server-side rendering,
    /// or the prerender of an interactive page before its circuit connects) and <see cref="DashboardView{T}.SyncUrl"/> is on,
    /// so the URL is the state and a link to the URL of the changed selections is the click (design §9). Components
    /// check it and hosts rendering their own clickable content can do the same, building the link with <see cref="Href"/>.
    /// </summary>
    public bool Links { get; private set; }

    /// <summary>
    /// The URL of this page with <paramref name="selections"/> written over the view's query parameters, the page's other
    /// parameters kept except those named in <see cref="DashboardView{T}.ResetOnChange"/>. Available whenever the view has
    /// <see cref="DashboardView{T}.SyncUrl"/>, whether or not <see cref="Links"/> is on, for a host that renders its own links.
    /// </summary>
    public string Href(Selections selections)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (hrefFor is null)
        {
            throw new InvalidOperationException("Links need the URL as the state: set SyncUrl on the DashboardView.");
        }

        return hrefFor(selections);
    }

    /// <summary>Set by the view: whether clicks render as links, and how a link for given selections is written.</summary>
    internal void ConfigureLinks(bool links, Func<Selections, string>? hrefFor)
    {
        Links = links;
        this.hrefFor = hrefFor;
    }

    /// <summary>Wall time of the most recent <c>Calculate</c>, including the initial one. A cache hit reads as near zero.</summary>
    public TimeSpan LastCalculation { get; private set; }

    /// <summary>Replaces the selections, recalculates, notifies components and the host: selections first, then the state.</summary>
    public async Task ApplyAsync(Selections selections)
    {
        ArgumentNullException.ThrowIfNull(selections);
        if (selections.Equals(Selections))
        {
            return;
        }

        Recalculate(selections);
        await onSelectionsChanged(selections);
        await onStateChanged(State);
    }

    /// <summary>The click on a value facet: add the value if absent, remove it if present.</summary>
    public Task ToggleAsync(string key, object? value) => ApplyAsync(Selections.Toggle(key, value));

    /// <summary>The click on a range facet's bar: add the interval if absent, remove it if present (concept §5).</summary>
    public Task ToggleIntervalAsync(string key, RangeInterval interval) => ApplyAsync(Selections.ToggleInterval(key, interval));

    /// <summary>The click on a date facet's bar or preset: add the part if absent, remove it if present (concept §5).</summary>
    public Task ToggleIntervalAsync(string key, DateInterval interval) => ApplyAsync(Selections.ToggleInterval(key, interval));

    /// <summary>A slider change, a null toggle or any other whole selection: replace the facet's selection. An empty one clears it.</summary>
    public Task SelectAsync(string key, Selection selection) => ApplyAsync(Selections.With(key, selection));

    /// <summary>Removes one facet's selection: a header's clear button or a chip.</summary>
    public Task ClearAsync(string key) => ApplyAsync(Selections.Clear(key));

    /// <summary>Removes every selection.</summary>
    public Task ClearAllAsync() => ApplyAsync(Selections.Empty);

    /// <summary>Adopts selections set by the host through the component parameter, without echoing them back. The host still hears about the new state.</summary>
    internal Task SyncAsync(Selections selections)
    {
        if (selections.Equals(Selections))
        {
            return Task.CompletedTask;
        }

        Recalculate(selections);
        return onStateChanged(State);
    }

    /// <summary>
    /// Adopts another dashboard or formatter from the view's parameters, recalculates with the given selections and
    /// notifies components and the host. The context instance is kept, so every component inside keeps its
    /// subscription and follows the change; a scoped dashboard (concept §4.10) thus updates every tile and facet.
    /// </summary>
    internal Task RebindAsync(Dashboard<T> dashboard, IDashboardFormatter formatter, Selections selections)
    {
        Dashboard = dashboard;
        Formatter = formatter;
        Recalculate(selections);
        return onStateChanged(State);
    }

    private void Recalculate(Selections selections)
    {
        Selections = selections;
        State = TimedCalculate(selections);
        items = null;
        StateChanged?.Invoke();
    }

    private DashboardState<T> TimedCalculate(Selections selections)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        DashboardState<T> state = Dashboard.Calculate(selections);
        LastCalculation = System.Diagnostics.Stopwatch.GetElapsedTime(start);
        return state;
    }
}
