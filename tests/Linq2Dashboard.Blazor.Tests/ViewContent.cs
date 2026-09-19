using Bunit;
using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests;

/// <summary>
/// Puts a component, or a fragment, inside a <see cref="DashboardView{T}"/> under test. The view's child content is a
/// template over the <see cref="DashboardContext{T}"/> (design §9), which bUnit's <c>AddChildContent</c> does not cover.
/// </summary>
internal static class ViewContent
{
    /// <summary>Renders one <typeparamref name="TChild"/> with the given parameters as the view's content.</summary>
    public static ComponentParameterCollectionBuilder<DashboardView<T>> AddContent<T, TChild>(
        this ComponentParameterCollectionBuilder<DashboardView<T>> parameters,
        Action<ComponentParameterCollectionBuilder<TChild>>? configure = null)
        where TChild : IComponent =>
        parameters.Add<TChild, DashboardContext<T>>(view => view.ChildContent, _ => child => configure?.Invoke(child));

    /// <summary>Renders one <typeparamref name="TChild"/> inside a view over <see cref="Order"/> rows, which is what most tests use.</summary>
    public static ComponentParameterCollectionBuilder<DashboardView<Order>> AddContent<TChild>(
        this ComponentParameterCollectionBuilder<DashboardView<Order>> parameters,
        Action<ComponentParameterCollectionBuilder<TChild>>? configure = null)
        where TChild : IComponent =>
        parameters.AddContent<Order, TChild>(configure);

    /// <summary>Renders a fragment that does not need the context as the view's content.</summary>
    public static ComponentParameterCollectionBuilder<DashboardView<T>> AddContent<T>(
        this ComponentParameterCollectionBuilder<DashboardView<T>> parameters,
        RenderFragment fragment) =>
        parameters.Add(view => view.ChildContent, (RenderFragment<DashboardContext<T>>)(_ => fragment));
}
