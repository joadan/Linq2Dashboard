using Linq2Dashboard.SampleData;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Sample.Components.Pages;

/// <summary>
/// The dashboard under static server-side rendering: the page has no render mode, so every request renders once from
/// the URL and every click is a link. The rows are paged by hand through a <c>page</c> query parameter, which the view
/// drops on a selection change through <c>ResetOnChange</c>; QuickGrid pages this way by itself from .NET 11.
/// </summary>
public partial class Static
{
    private const int PageSize = 50;

    private Dashboard<SampleOrder>? dashboard;

    /// <summary>The one-based page from the URL; absent or malformed reads as the first page.</summary>
    [SupplyParameterFromQuery(Name = "page")]
    public int? PageParameter { get; set; }

    private int Page => Math.Max(1, PageParameter ?? 1);

    /// <summary>The shared dashboard from the cache service. Static rendering waits for it before responding.</summary>
    private Dashboard<SampleOrder> Dashboard => dashboard ?? throw new InvalidOperationException("The dashboard has not loaded yet.");

    protected override async Task OnInitializedAsync() => dashboard = await Dashboards.GetAsync();

    /// <summary>This URL with the page parameter replaced; the first page carries none.</summary>
    private string PageHref(int page) => Navigation.GetUriWithQueryParameter("page", page > 1 ? page : null);
}
