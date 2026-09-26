using Linq2Dashboard.Tests;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests.Cascading;

/// <summary>A view over a built dashboard whose children leave out <c>T</c> (design §9).</summary>
public partial class BuiltPage
{
    [Parameter, EditorRequired]
    public Dashboard<Order> Dashboard { get; set; } = null!;

    [Parameter, EditorRequired]
    public IDashboardFormatter Formatter { get; set; } = null!;
}
