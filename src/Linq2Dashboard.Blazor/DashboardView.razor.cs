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

    /// <summary>The dashboard to render. Building it is the host's job (concept §4.9).</summary>
    [Parameter, EditorRequired]
    public Dashboard<T> Dashboard { get; set; } = default!;

    /// <summary>
    /// The current selections. Bindable: <c>@@bind-Selections</c> keeps the host informed of every
    /// click, and setting it from the host applies new selections, for example from a bookmark.
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

    /// <summary>Text formatting for every component inside. Defaults to <see cref="DefaultDashboardFormatter"/>.</summary>
    [Parameter]
    public IDashboardFormatter? Formatter { get; set; }

    /// <summary>The layout. Components inside receive the <see cref="DashboardContext{T}"/> as a cascading parameter. Defaults to <see cref="StateSummary{T}"/>.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

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
        ArgumentNullException.ThrowIfNull(Dashboard);
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
                Selections fromUrl = Dashboard.Serializer.FromQueryString(Navigation.Uri, QueryPrefix);
                if (!fromUrl.IsEmpty)
                {
                    initial = fromUrl;
                }
            }

            context = new DashboardContext<T>(Dashboard, initial, formatter, OnSelectionsChangedAsync, OnStateChangedAsync);
            contextDashboard = Dashboard;
            contextFormatter = formatter;
            lastSelectionsParameter = Selections;
            if (!initial.Equals(selections))
            {
                lastSelectionsParameter = initial;
                await SelectionsChanged.InvokeAsync(initial);
            }

            WriteUrl();
            await OnStateChangedAsync(context.State);
            return;
        }

        // Another dashboard, typically a scope of the first (concept §4.10), or another formatter: the same context
        // adopts it, so the components inside, which subscribed to this context once, all follow the change.
        // With the URL in charge the selections stay what the URL says; otherwise the parameter decides.
        if (!ReferenceEquals(contextDashboard, Dashboard) || !ReferenceEquals(contextFormatter, formatter))
        {
            contextDashboard = Dashboard;
            contextFormatter = formatter;
            lastSelectionsParameter = Selections;
            await context.RebindAsync(Dashboard, formatter, SyncUrl ? context.Selections : selections);
            WriteUrl();
            return;
        }

        if (!Equals(Selections, lastSelectionsParameter))
        {
            lastSelectionsParameter = Selections;
            await context.SyncAsync(selections);
            WriteUrl();
        }
    }

    private async Task OnSelectionsChangedAsync(Selections selections)
    {
        lastSelectionsParameter = selections;
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

        SelectionSerializer serializer = context.Dashboard.Serializer;
        if (serializer.FromQueryString(Navigation.Uri, QueryPrefix).Equals(context.Selections))
        {
            return;
        }

        var uri = new Uri(Navigation.Uri);
        string query = serializer.ToQueryString(context.Selections, QueryPrefix, uri.Query);
        string target = uri.GetLeftPart(UriPartial.Path) + (query.Length > 0 ? "?" + query : string.Empty) + uri.Fragment;
        Navigation.NavigateTo(target, replace: true);
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
