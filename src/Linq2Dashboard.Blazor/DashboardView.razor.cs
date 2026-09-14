using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

public partial class DashboardView<T>
{
    private DashboardContext<T>? context;
    private Dashboard<T>? contextDashboard;
    private IDashboardFormatter? contextFormatter;
    private Selections? lastSelectionsParameter;

    /// <summary>The dashboard to render. Building it is the host's job (concept §4.9).</summary>
    [Parameter, EditorRequired]
    public Dashboard<T> Dashboard { get; set; } = default!;

    /// <summary>
    /// The current selections. Bindable: <c>@@bind-Selections</c> keeps the host informed of every
    /// click, and setting it from the host applies new selections, for example from a bookmark.
    /// </summary>
    [Parameter]
    public Selections? Selections { get; set; }

    [Parameter]
    public EventCallback<Selections> SelectionsChanged { get; set; }

    /// <summary>
    /// Raised with the new <see cref="DashboardState{T}"/> after every calculation: the initial one, every click,
    /// and selections set by the host. On a click it follows <see cref="SelectionsChanged"/>. For hosts that
    /// render something of their own from the result, such as a chart, without reaching into the context.
    /// </summary>
    [Parameter]
    public EventCallback<DashboardState<T>> StateChanged { get; set; }

    /// <summary>Text formatting for every component inside. Defaults to <see cref="DefaultDashboardFormatter"/>.</summary>
    [Parameter]
    public IDashboardFormatter? Formatter { get; set; }

    /// <summary>The layout. Components inside receive the <see cref="DashboardContext{T}"/> as a cascading parameter. Defaults to <see cref="StateSummary{T}"/>.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>The shared context, for hosts that render their own children.</summary>
    public DashboardContext<T> Context => context ?? throw new InvalidOperationException("The component has not received its parameters yet.");

    protected override Task OnParametersSetAsync()
    {
        ArgumentNullException.ThrowIfNull(Dashboard);
        IDashboardFormatter formatter = Formatter ?? DefaultDashboardFormatter.Instance;
        Selections selections = Selections ?? Linq2Dashboard.Selections.Empty;

        if (context is null || !ReferenceEquals(contextDashboard, Dashboard) || !ReferenceEquals(contextFormatter, formatter))
        {
            context = new DashboardContext<T>(Dashboard, selections, formatter, OnSelectionsChangedAsync, OnStateChangedAsync);
            contextDashboard = Dashboard;
            contextFormatter = formatter;
            lastSelectionsParameter = Selections;
            return OnStateChangedAsync(context.State);
        }

        if (!Equals(Selections, lastSelectionsParameter))
        {
            lastSelectionsParameter = Selections;
            return context.SyncAsync(selections);
        }

        return Task.CompletedTask;
    }

    private async Task OnSelectionsChangedAsync(Selections selections)
    {
        lastSelectionsParameter = selections;
        await SelectionsChanged.InvokeAsync(selections);
        StateHasChanged();
    }

    private Task OnStateChangedAsync(DashboardState<T> state) => StateChanged.InvokeAsync(state);
}
