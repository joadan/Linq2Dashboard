using Microsoft.AspNetCore.Components;
using System.Text.RegularExpressions;
using Markdig;

namespace Linq2Dashboard.Docs.Shared;

public partial class MarkdownDocument
{

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseAutoIdentifiers()
        .Build();

    private MarkupString? html;
    private string? error;
    private string? loadedPath;

    /// <summary>Path under wwwroot, for example <c>docs/concept.md</c>.</summary>
    [Parameter, EditorRequired]
    public string Path { get; set; } = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        if (Path == loadedPath)
        {
            return;
        }

        loadedPath = Path;
        html = null;
        error = null;
        try
        {
            string markdown = await Http.GetStringAsync(Path);
            html = new MarkupString(FrameCodeBlocks(Markdown.ToHtml(RewriteLinks(markdown), Pipeline)));
        }
        catch (HttpRequestException e)
        {
            error = $"Could not load {Path}: {e.Message}";
        }
    }

    /// <summary>The documents link to each other by file name in the repository; on the site they are pages.</summary>
    private static string RewriteLinks(string markdown) => markdown
        .Replace("](Linq2Dashboard-concept.md)", "](concept)")
        .Replace("](Linq2Dashboard-design.md)", "](design)")
        .Replace("](Linq2Dashboard-usage.md)", "](usage)");

    /// <summary>
    /// Gives every fenced block the frame <see cref="CodeBlock"/> renders: a header with the language and a copy button.
    /// The language class is mapped to the grammar highlight.js knows, or dropped for plain text.
    /// </summary>
    private static string FrameCodeBlocks(string html) => CodeBlockStart()
        .Replace(html, match =>
        {
            string? language = match.Groups[1].Success ? match.Groups[1].Value : null;
            string? highlighter = CodeLanguages.Highlighter(language);
            string label = System.Net.WebUtility.HtmlEncode(CodeLanguages.Label(language));
            string codeClass = highlighter is null ? string.Empty : $" class=\"language-{highlighter}\"";
            return $"<div class=\"code-block\"><div class=\"code-block-head\"><span class=\"code-block-label\">{label}</span>{CodeLanguages.CopyButton}</div><pre><code{codeClass}>";
        })
        .Replace("</code></pre>", "</code></pre></div>");

    [GeneratedRegex("""<pre><code(?: class="language-([^"]+)")?>""")]
    private static partial Regex CodeBlockStart();
}
