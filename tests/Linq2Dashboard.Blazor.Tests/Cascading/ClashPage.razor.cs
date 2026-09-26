using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests.Cascading;

/// <summary>Another library's generic components inside a view, their <c>T</c> left to inference.</summary>
public partial class ClashPage
{
    [Parameter, EditorRequired]
    public IReadOnlyList<Order> Items { get; set; } = null!;

    [Parameter, EditorRequired]
    public IDashboardFormatter Formatter { get; set; } = null!;
}
