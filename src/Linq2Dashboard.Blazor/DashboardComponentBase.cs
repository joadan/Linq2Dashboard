using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// Base for every component rendered inside a <see cref="DashboardView{T}"/>. Receives the
/// cascaded <see cref="DashboardContext{T}"/> and re-renders whenever the state changes.
/// </summary>
public abstract class DashboardComponentBase<T> : ComponentBase, IDisposable
{
    [CascadingParameter]
    public DashboardContext<T> Context { get; set; } = default!;

    protected DashboardState<T> State => Context.State;

    protected IDashboardFormatter Formatter => Context.Formatter;

    protected override void OnInitialized()
    {
        if (Context is null)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} must be placed inside a DashboardView<{typeof(T).Name}>.");
        }

        Context.StateChanged += OnStateChanged;
    }

    public void Dispose()
    {
        if (Context is not null)
        {
            Context.StateChanged -= OnStateChanged;
        }

        GC.SuppressFinalize(this);
    }

    private void OnStateChanged() => InvokeAsync(StateHasChanged);
}
