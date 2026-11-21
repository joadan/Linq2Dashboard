namespace Linq2Dashboard.Docs.Layout;

/// <summary>One page in the sidebar. <see cref="Href"/> is base-relative, empty for the home page.</summary>
public sealed record DocsPage(string Title, string Href);

/// <summary>A titled group of pages in the sidebar.</summary>
public sealed record DocsSection(string Title, IReadOnlyList<DocsPage> Pages);

/// <summary>The site's page order: the sidebar renders it and the previous and next links follow it.</summary>
public static class DocsNavigation
{
    /// <summary>The sidebar groups, in reading order.</summary>
    public static readonly IReadOnlyList<DocsSection> Sections =
    [
        new("Start here",
        [
            new("Overview", ""),
            new("Live demo", "demo"),
            new("Try it in five minutes", "five-minutes"),
        ]),
        new("Guides",
        [
            new("Getting started", "getting-started"),
            new("Cache a shared dashboard", "cached-dashboard"),
            new("Ask it with an agent", "agents"),
        ]),
        new("Reference",
        [
            new("Usage", "usage"),
            new("Concept", "concept"),
            new("Design", "design"),
        ]),
    ];

    private static readonly IReadOnlyList<DocsPage> Pages = Sections.SelectMany(s => s.Pages).ToList();

    /// <summary>The position of the page at a base-relative path without query or fragment, or -1 for a path not in the sidebar.</summary>
    public static int IndexOf(string path)
    {
        for (int i = 0; i < Pages.Count; i++)
        {
            if (string.Equals(Pages[i].Href, path, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The page before the given one, or null at the start.</summary>
    public static DocsPage? Previous(int index) => index > 0 ? Pages[index - 1] : null;

    /// <summary>The page after the given one, or null at the end.</summary>
    public static DocsPage? Next(int index) => index >= 0 && index < Pages.Count - 1 ? Pages[index + 1] : null;
}
