using Microsoft.AspNetCore.Components;
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
            html = new MarkupString(Markdown.ToHtml(RewriteLinks(markdown), Pipeline));
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
}
