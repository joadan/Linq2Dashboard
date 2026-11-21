namespace Linq2Dashboard.Docs.Shared;

/// <summary>The languages the site's code samples use: their header label and the highlight.js grammar for each.</summary>
public static class CodeLanguages
{
    /// <summary>
    /// The copy button, shared by <see cref="CodeBlock"/> and the rendered markdown. js/site.js handles the click and
    /// marks the button data-copied for a moment; the stylesheet swaps the two labels.
    /// </summary>
    public const string CopyButton =
        """<button type="button" class="code-copy" aria-label="Copy the code">"""
        + """<span class="code-copy-idle"><svg viewBox="0 0 24 24" aria-hidden="true"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 01-2-2V4a2 2 0 012-2h9a2 2 0 012 2v1"/></svg>Copy</span>"""
        + """<span class="code-copy-done"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M20 6L9 17l-5-5"/></svg>Copied</span>"""
        + "</button>";

    /// <summary>The label shown in a code block's header.</summary>
    public static string Label(string? language) => language?.ToLowerInvariant() switch
    {
        "csharp" or "cs" or "c#" => "C#",
        "razor" or "cshtml" => "Razor",
        "shell" or "bash" or "sh" or "powershell" => "Terminal",
        "css" => "CSS",
        "json" => "JSON",
        "xml" or "html" => "HTML",
        null or "" or "text" or "plaintext" => "Text",
        var other => other,
    };

    /// <summary>The highlight.js grammar, or null to leave the block unhighlighted. Razor reads well enough as markup.</summary>
    public static string? Highlighter(string? language) => language?.ToLowerInvariant() switch
    {
        "csharp" or "cs" or "c#" => "csharp",
        "razor" or "cshtml" or "xml" or "html" => "xml",
        "shell" or "bash" or "sh" or "powershell" => "bash",
        "css" => "css",
        "json" => "json",
        _ => null,
    };
}
