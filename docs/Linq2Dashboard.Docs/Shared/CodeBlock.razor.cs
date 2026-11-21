using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Docs.Shared;

/// <summary>
/// A code sample in a framed block. Only a block with a <see cref="Language"/> is highlighted, and its code must not
/// change after the first render: highlight.js replaces the text node Blazor would otherwise update.
/// </summary>
public partial class CodeBlock
{
    /// <summary>The code, shown as it is.</summary>
    [Parameter, EditorRequired]
    public string Code { get; set; } = string.Empty;

    /// <summary>csharp, razor, shell, css or json; null for plain text that may change, such as a tool's output.</summary>
    [Parameter]
    public string? Language { get; set; }

    /// <summary>Shown in the header instead of the language, for example a file name.</summary>
    [Parameter]
    public string? Title { get; set; }

    /// <summary>Extra classes on the frame.</summary>
    [Parameter]
    public string? Class { get; set; }

    private string? CodeClass => CodeLanguages.Highlighter(Language) is { } highlighter ? "language-" + highlighter : null;
}
