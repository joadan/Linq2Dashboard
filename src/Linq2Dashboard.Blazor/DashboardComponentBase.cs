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

    /// <summary>
    /// Extra classes for the component's root element, rendered after the library's own <c>l2d-*</c>
    /// classes so a host can target one component from its own stylesheet (design §9). Parameter names
    /// match case-insensitively, so <c>class="..."</c> on the component lands here too.
    /// </summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>
    /// Every attribute written on the component that is not a parameter (<c>id</c>, <c>style</c>, <c>title</c>,
    /// <c>data-*</c>, <c>aria-*</c>) is rendered on the root element, before the library's own attributes,
    /// so <c>class</c> and <c>data-key</c> cannot be overridden this way (design §9).
    /// </summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The root element's class attribute: the library's classes followed by <see cref="Class"/>.</summary>
    protected string RootClass(string libraryClasses) =>
        string.IsNullOrWhiteSpace(Class) ? libraryClasses : $"{libraryClasses} {Class.Trim()}";

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

    /// <summary>Called on the renderer's thread when the state changed, before re-rendering. Override to reset component-local UI state such as a page index.</summary>
    protected virtual void OnDashboardStateChanged()
    {
    }

    private void OnStateChanged() => InvokeAsync(() =>
    {
        OnDashboardStateChanged();
        StateHasChanged();
    });
}
