using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// Base for every component rendered inside a <see cref="DashboardView{T}"/>. Receives the
/// cascaded <see cref="DashboardContext{T}"/> and re-renders whenever the state changes.
/// </summary>
public abstract class DashboardComponentBase<T> : ComponentBase, IDisposable
{
    /// <summary>The shared context from the enclosing <see cref="DashboardView{T}"/>. Cascaded; never set by hand.</summary>
    [CascadingParameter]
    public DashboardContext<T> Context { get; set; } = default!;

    /// <summary>The current state, from the context.</summary>
    protected DashboardState<T> State => Context.State;

    /// <summary>The formatter, from the context.</summary>
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
    protected string RootClass(string libraryClasses) => CssClasses.Join(libraryClasses, Class);

    /// <summary>
    /// The link a click renders as when <see cref="DashboardContext{T}.Links"/> is on: the URL of the selections that
    /// <paramref name="change"/> makes from the current ones. Null when clicks are buttons, so the change is not even
    /// computed in an interactive view (design §9).
    /// </summary>
    protected string? LinkTo(Func<Selections, Selections> change) =>
        Context.Links ? Context.Href(change(Context.Selections)) : null;

    /// <summary>Subscribes to the context; throws when the component is not inside a <see cref="DashboardView{T}"/>.</summary>
    protected override void OnInitialized()
    {
        if (Context is null)
        {
            throw new InvalidOperationException(
                $"{GetType().Name} must be placed inside a DashboardView<{typeof(T).Name}>.");
        }

        Context.StateChanged += OnStateChanged;
    }

    /// <summary>Stops listening to the context, then calls <see cref="Disposing"/>.</summary>
    public void Dispose()
    {
        if (Context is not null)
        {
            Context.StateChanged -= OnStateChanged;
        }

        Disposing();
        GC.SuppressFinalize(this);
    }

    /// <summary>Called once when the component is disposed, after it stopped listening to the context. Override to release component-local resources such as a timer.</summary>
    protected virtual void Disposing()
    {
    }

    /// <summary>Called on the renderer's thread when the state changed, before re-rendering. Override to reset component-local UI state such as a page index.</summary>
    protected virtual void OnDashboardStateChanged()
    {
    }

    /// <summary>
    /// The error for a facet of another kind than this component renders: names the kind and the component for it, so a
    /// key that is right but on the wrong component is a one-line fix (design §9).
    /// </summary>
    protected InvalidOperationException WrongKind(FacetState facet)
    {
        string? component = facet.Kind switch
        {
            FacetKind.Value or FacetKind.Boolean or FacetKind.MultiValue => "ValueFacet",
            FacetKind.Range => "RangeFacet",
            FacetKind.Date => "DateFacet",
            FacetKind.Text => "TextFacet",
            _ => null,
        };
        string use = component is null ? "a component for that kind" : component;
        return new InvalidOperationException($"Facet '{facet.Key}' is a {facet.Kind} facet, which {ComponentName} does not render; use {use}.");
    }

    /// <summary>The component's name without the generic arity suffix, for messages.</summary>
    protected string ComponentName
    {
        get
        {
            string name = GetType().Name;
            int tick = name.IndexOf('`');
            return tick < 0 ? name : name[..tick];
        }
    }

    private void OnStateChanged() => InvokeAsync(() =>
    {
        OnDashboardStateChanged();
        StateHasChanged();
    });
}
