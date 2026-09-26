using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;

namespace Linq2Dashboard.Blazor;

/// <summary>The root of a dashboard UI (design §9). Owns the selections and the state, cascades a <see cref="DashboardContext{T}"/> to every component inside, and counts nothing itself.</summary>
public partial class DashboardView<T> : IDisposable
{
    private DashboardContext<T>? context;
    private Dashboard<T>? contextDashboard;
    private IDashboardFormatter? contextFormatter;
    private Selections? lastSelectionsParameter;
    private bool listening;
    private string? pagePath;
    private Dashboard<T>? built;
    private IReadOnlyList<T>? builtItems;
    private object? builtRebuildKey;

    /// <summary>
    /// A dashboard built by the host, typically once and shared by every user (concept §7). The view renders it and
    /// builds nothing. Give the view either this or <see cref="Items"/>, never both.
    /// </summary>
    [Parameter]
    public Dashboard<T>? Dashboard { get; set; }

    /// <summary>
    /// Rows the view builds its own dashboard over, with <see cref="Build"/> as the definition: for a small dataset
    /// of the user's own rows, built on every visit and never cached (concept §7, design §9). The view builds when the
    /// list reference changes, so hold the list in a field; a list created in the markup is a new reference on every
    /// render and rebuilds every time. Give the view either this or <see cref="Dashboard"/>, never both.
    /// </summary>
    [Parameter]
    public IReadOnlyList<T>? Items { get; set; }

    /// <summary>
    /// The definition of the dashboard the view builds over <see cref="Items"/>: the same builder as
    /// <see cref="Linq2Dashboard.Dashboard.Create{T}"/>, so fixed filters, sort order and the time provider work as
    /// there. Read at every build, which happens when <see cref="Items"/> or <see cref="RebuildKey"/> changes, not when
    /// the delegate does. Needs <see cref="Items"/>.
    /// </summary>
    [Parameter]
    public Action<DashboardBuilder<T>>? Build { get; set; }

    /// <summary>
    /// Any value; when it changes, the view builds again over the same <see cref="Items"/>. For a <see cref="Build"/> that
    /// reads page state, such as the current language for a label. Compared with <see cref="object.Equals(object?, object?)"/>.
    /// Needs <see cref="Items"/>.
    /// </summary>
    [Parameter]
    public object? RebuildKey { get; set; }

    /// <summary>
    /// The current selections. Bindable: <c>@@bind-Selections</c> keeps the host informed of every
    /// click, and setting it from the host applies new selections, for example from a bookmark. Optional: only a
    /// changed value applies, so a host that leaves it out, or re-renders with the same value, never undoes a
    /// click (design §9).
    /// </summary>
    [Parameter]
    public Selections? Selections { get; set; }

    /// <summary>Raised with the new selections after every click, for <c>@bind-Selections</c> and for bookmarking.</summary>
    [Parameter]
    public EventCallback<Selections> SelectionsChanged { get; set; }

    /// <summary>
    /// Raised with the new <see cref="DashboardState{T}"/> after every calculation: the initial one, every click,
    /// and selections set by the host. On a click it follows <see cref="SelectionsChanged"/>. For hosts that
    /// render something of their own from the result, such as a chart, without reaching into the context.
    /// </summary>
    [Parameter]
    public EventCallback<DashboardState<T>> StateChanged { get; set; }

    /// <summary>
    /// The view's identity on the page, rendered as <c>data-key</c> on the root element. With <see cref="SyncUrl"/>
    /// it prefixes the view's query parameters as <c>key.facetKey</c>, so two views on one page keep their
    /// selections apart; a page with one view needs none (design §2.5, §9).
    /// </summary>
    [Parameter]
    public string? Key { get; set; }

    /// <summary>
    /// Keeps the selections in the page URL, one query parameter per facet in the readable form of
    /// <see cref="SelectionSerializer.ToQueryString"/> (design §2.5). On first render the URL wins: selections found
    /// under this view's <see cref="Key"/> apply and <see cref="SelectionsChanged"/> reports them; otherwise
    /// <see cref="Selections"/> applies. Every change replaces the URL in place, keeping the page's other
    /// parameters; a navigation that changes this page's query applies to the view. Only an interactive renderer
    /// writes, so prerendering reads the URL and leaves it alone. Off by default.
    /// </summary>
    [Parameter]
    public bool SyncUrl { get; set; }

    /// <summary>
    /// Query parameters of the page that a selection change drops, comma-separated, for example <c>page</c>: a new
    /// filter has a new first page. Applies to the URL the view writes and to the links it renders when not interactive;
    /// every other parameter of the page is kept as it is. Needs <see cref="SyncUrl"/> (design §9).
    /// </summary>
    [Parameter]
    public string? ResetOnChange { get; set; }

    /// <summary>Text formatting for every component inside. Defaults to <see cref="DefaultDashboardFormatter"/>.</summary>
    [Parameter]
    public IDashboardFormatter? Formatter { get; set; }

    /// <summary>
    /// The layout: a template over the <see cref="DashboardContext{T}"/>, so the page can hand the matching rows to its own
    /// grid (<c>Items="context.Items"</c>) and reach the state without a <c>@@ref</c>. Components inside also receive the
    /// context as a cascading parameter. Defaults to <see cref="StateSummary{T}"/>.
    /// </summary>
    [Parameter]
    public RenderFragment<DashboardContext<T>>? ChildContent { get; set; }

    /// <summary>Extra classes for the root element, after <c>l2d-dashboard</c> (design §9).</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Every attribute that is not a parameter is rendered on the root element, before the library's own, so <c>class</c> cannot be overridden this way (design §9).</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The shared context, for hosts that render their own children.</summary>
    public DashboardContext<T> Context => context ?? throw new InvalidOperationException("The component has not received its parameters yet.");

    private string RootClass => string.IsNullOrWhiteSpace(Class) ? "l2d-dashboard" : $"l2d-dashboard {Class.Trim()}";

    /// <summary>The query-parameter prefix: the key and a dot, or nothing.</summary>
    private string QueryPrefix => string.IsNullOrEmpty(Key) ? string.Empty : Key + ".";

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        Dashboard<T> dashboard = ResolveDashboard(out bool rebuilt);
        IDashboardFormatter formatter = Formatter ?? DefaultDashboardFormatter.Instance;
        Selections selections = Selections ?? Linq2Dashboard.Selections.Empty;

        if (SyncUrl && !listening)
        {
            Navigation.LocationChanged += OnLocationChanged;
            listening = true;
        }

        if (context is null)
        {
            // The URL wins on first render when it has selections for this view; the parameter otherwise.
            Selections initial = selections;
            if (SyncUrl)
            {
                pagePath = new Uri(Navigation.Uri).AbsolutePath;
                Selections fromUrl = dashboard.Serializer.FromQueryString(Navigation.Uri, QueryPrefix);
                if (!fromUrl.IsEmpty)
                {
                    initial = fromUrl;
                }
            }

            context = new DashboardContext<T>(dashboard, initial, formatter, OnSelectionsChangedAsync, OnStateChangedAsync);
            ConfigureLinks();
            contextDashboard = dashboard;
            contextFormatter = formatter;
            lastSelectionsParameter = Selections;
            if (!initial.Equals(selections))
            {
                await SelectionsChanged.InvokeAsync(initial);
            }

            WriteUrl();
            await OnStateChangedAsync(context.State);
            return;
        }

        ConfigureLinks();

        // The parameter is compared with the value it last had, not with the current selections: a host that does not
        // bind Selections re-renders with the same value after every click, through the router when SyncUrl navigates,
        // and that must not read as the host clearing the selections.
        bool parameterChanged = !Equals(Selections, lastSelectionsParameter);
        lastSelectionsParameter = Selections;

        // Another dashboard, typically a scope of the first (concept §4.10), or another formatter: the same context
        // adopts it, so the components inside, which subscribed to this context once, all follow the change.
        // With the URL in charge the selections stay what the URL says; otherwise a changed parameter decides.
        if (!ReferenceEquals(contextDashboard, dashboard) || !ReferenceEquals(contextFormatter, formatter))
        {
            Selections next = SyncUrl || !parameterChanged ? context.Selections : selections;

            // A dashboard the view built itself gets the selections the way a bookmark would carry them to new data
            // (concept §4.9): what its definition cannot read is dropped, and a bound host hears about it.
            Selections carried = rebuilt ? CarryOver(next, context.Dashboard, dashboard) : next;
            contextDashboard = dashboard;
            contextFormatter = formatter;
            await context.RebindAsync(dashboard, formatter, carried);
            if (!carried.Equals(next))
            {
                await SelectionsChanged.InvokeAsync(carried);
            }

            WriteUrl();
            return;
        }

        if (parameterChanged)
        {
            await context.SyncAsync(selections);
            WriteUrl();
        }
    }

    /// <summary>
    /// The dashboard to render: the host's <see cref="Dashboard"/>, or the one this view builds over <see cref="Items"/>
    /// with <see cref="Build"/>, built again when the list reference or <see cref="RebuildKey"/> changes (design §9).
    /// Exactly one of the two must be given; <see cref="Build"/> and <see cref="RebuildKey"/> belong to <see cref="Items"/>.
    /// </summary>
    private Dashboard<T> ResolveDashboard(out bool rebuilt)
    {
        rebuilt = false;
        if (Dashboard is not null && Items is not null)
        {
            throw new InvalidOperationException(
                "DashboardView takes either Dashboard, a dashboard the host built, or Items, rows the view builds a dashboard over; not both.");
        }

        if (Dashboard is not null)
        {
            if (Build is not null || RebuildKey is not null)
            {
                throw new InvalidOperationException(
                    "Build and RebuildKey apply to Items. A Dashboard given to the view is already built; define it where it is created.");
            }

            return Dashboard;
        }

        if (Items is null)
        {
            throw new InvalidOperationException(
                "DashboardView needs Dashboard, a dashboard the host built, or Items, rows the view builds a dashboard over.");
        }

        if (built is null || !ReferenceEquals(builtItems, Items) || !Equals(builtRebuildKey, RebuildKey))
        {
            rebuilt = built is not null;
            built = Linq2Dashboard.Dashboard.Create(Items, Build ?? (_ => { }));
            builtItems = Items;
            builtRebuildKey = RebuildKey;
        }

        return built;
    }

    /// <summary>
    /// <paramref name="selections"/> moved from one built dashboard to the next as a bookmark would move them: written by
    /// the old serializer and read by the new, so a facet the new definition lacks, or a value it cannot read, is dropped
    /// rather than thrown on (design §2.5). The same definition over new rows keeps every selection.
    /// </summary>
    private static Selections CarryOver(Selections selections, Dashboard<T> from, Dashboard<T> to) =>
        selections.IsEmpty ? selections : to.Serializer.FromJson(from.Serializer.ToJson(selections));

    private async Task OnSelectionsChangedAsync(Selections selections)
    {
        WriteUrl();
        await SelectionsChanged.InvokeAsync(selections);
        StateHasChanged();
    }

    private Task OnStateChangedAsync(DashboardState<T> state) => StateChanged.InvokeAsync(state);

    /// <summary>
    /// Replaces the current URL with one whose parameters for this view say the context's selections, keeping
    /// every other parameter and the fragment. Nothing happens when the URL already reads the same, so the
    /// navigation this raises does not come back as a change, or when the renderer cannot navigate (prerendering).
    /// </summary>
    private void WriteUrl()
    {
        if (!SyncUrl || context is null || !RendererInfo.IsInteractive)
        {
            return;
        }

        if (context.Dashboard.Serializer.FromQueryString(Navigation.Uri, QueryPrefix).Equals(context.Selections))
        {
            return;
        }

        var uri = new Uri(Navigation.Uri);
        Navigation.NavigateTo(uri.GetLeftPart(UriPartial.Authority) + UrlFor(context.Selections), replace: true);
    }

    /// <summary>
    /// Clicks are links exactly when the renderer is not interactive and the URL is the state (design §9): static
    /// server-side rendering, or the prerender of an interactive page, where a button would do nothing until the
    /// circuit connects. The href builder is available whenever <see cref="SyncUrl"/> is on.
    /// </summary>
    private void ConfigureLinks() =>
        context?.ConfigureLinks(SyncUrl && !RendererInfo.IsInteractive, SyncUrl ? UrlFor : null);

    /// <summary>
    /// This page's path, query and fragment with <paramref name="selections"/> written over the view's parameters. The
    /// page's other parameters stay in place, except those named in <see cref="ResetOnChange"/>, which are dropped.
    /// </summary>
    private string UrlFor(Selections selections)
    {
        var uri = new Uri(Navigation.Uri);
        string query = Context.Dashboard.Serializer.ToQueryString(selections, QueryPrefix, WithoutReset(uri.Query));
        return uri.AbsolutePath + (query.Length > 0 ? "?" + query : string.Empty) + uri.Fragment;
    }

    /// <summary>The query without the parameters named in <see cref="ResetOnChange"/>; the rest verbatim, in order.</summary>
    private string WithoutReset(string query)
    {
        if (string.IsNullOrWhiteSpace(ResetOnChange) || query.Length == 0)
        {
            return query;
        }

        string[] reset = ResetOnChange.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> kept = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => !reset.Contains(Uri.UnescapeDataString(segment.Split('=', 2)[0]), StringComparer.Ordinal));
        return string.Join('&', kept);
    }

    /// <summary>A navigation within this page whose query says something else for this view applies it, as a click would: back, forward, or a link on the page.</summary>
    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (context is null || !SyncUrl || new Uri(e.Location).AbsolutePath != pagePath)
        {
            return;
        }

        Selections fromUrl = context.Dashboard.Serializer.FromQueryString(e.Location, QueryPrefix);
        if (fromUrl.Equals(context.Selections))
        {
            return;
        }

        DashboardContext<T> current = context;
        _ = InvokeAsync(() => current.ApplyAsync(fromUrl));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (listening)
        {
            Navigation.LocationChanged -= OnLocationChanged;
            listening = false;
        }
    }
}
