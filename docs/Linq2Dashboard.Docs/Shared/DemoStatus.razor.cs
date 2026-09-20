using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Docs.Shared;

public partial class DemoStatus
{
    /// <summary>The value the facet component hands its template: the raw value, both counts and the selected flag.</summary>
    [Parameter, EditorRequired]
    public FacetValue Value { get; set; } = default!;

    /// <summary>The status text; the stylesheet turns it into the dot's colour.</summary>
    private string Text => Value.Label ?? Value.Value?.ToString() ?? "None";

    private string Filtered => DemoFormatter.Instance.FormatCount(Value.FilteredCount);

    private string Total => DemoFormatter.Instance.FormatCount(Value.TotalCount);
}
