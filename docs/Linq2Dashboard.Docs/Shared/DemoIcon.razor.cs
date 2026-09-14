using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Docs.Shared;

public partial class DemoIcon
{
    /// <summary>Which icon to draw: cart, banknote, receipt, users or trend.</summary>
    [Parameter, EditorRequired]
    public string Name { get; set; } = string.Empty;

    private MarkupString Paths => new(Name switch
    {
        "cart" => "<circle cx=\"8\" cy=\"21\" r=\"1\"/><circle cx=\"19\" cy=\"21\" r=\"1\"/><path d=\"M2 2h2l2.7 12.4a2 2 0 0 0 2 1.6h9.7a2 2 0 0 0 2-1.6L22 7H5.1\"/>",
        "banknote" => "<rect x=\"2\" y=\"6\" width=\"20\" height=\"12\" rx=\"2\"/><circle cx=\"12\" cy=\"12\" r=\"2\"/><path d=\"M6 12h.01M18 12h.01\"/>",
        "receipt" => "<path d=\"M4 2v20l2-1 2 1 2-1 2 1 2-1 2 1 2-1 2 1V2l-2 1-2-1-2 1-2-1-2 1-2-1-2 1z\"/><path d=\"M14 8H9.5a1.5 1.5 0 0 0 0 3h5a1.5 1.5 0 0 1 0 3H9\"/><path d=\"M12 6.5v10\"/>",
        "users" => "<path d=\"M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2\"/><circle cx=\"9\" cy=\"7\" r=\"4\"/><path d=\"M22 21v-2a4 4 0 0 0-3-3.9\"/><path d=\"M16 3.1a4 4 0 0 1 0 7.8\"/>",
        "trend" => "<polyline points=\"22 7 13.5 15.5 8.5 10.5 2 17\"/><polyline points=\"16 7 22 7 22 13\"/>",
        _ => throw new ArgumentOutOfRangeException(nameof(Name), Name, "Unknown demo icon."),
    });
}
