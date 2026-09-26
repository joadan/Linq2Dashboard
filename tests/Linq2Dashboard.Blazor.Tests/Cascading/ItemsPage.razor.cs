using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests.Cascading;

/// <summary>A view with <c>Items</c> whose children define facets and metrics without <c>T</c>, except the one given a method group (design §9).</summary>
public partial class ItemsPage
{
    [Parameter, EditorRequired]
    public IReadOnlyList<Order> Items { get; set; } = null!;

    [Parameter, EditorRequired]
    public IDashboardFormatter Formatter { get; set; } = null!;

    // A method group does not carry the row type, so the component using it names T (design §9).
    private static bool MatchesCountry(Order order, string text) => order.Country == text;
}
