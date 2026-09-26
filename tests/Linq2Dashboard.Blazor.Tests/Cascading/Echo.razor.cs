using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor.Tests.Cascading;

/// <summary>A component of someone else's with a type parameter also named <c>T</c>, inferred from its value.</summary>
public partial class Echo<T>
{
    [Parameter]
    public T? Value { get; set; }
}
