using Linq2Dashboard.SampleData;

namespace Linq2Dashboard.Sample.Components.Pages;

public partial class Home
{
    private readonly Dictionary<string, Dashboard<SampleOrder>> scopes = new(StringComparer.Ordinal);
    private Dashboard<SampleOrder>? dashboard;
    private Selections selections = Selections.Empty;
    private string scope = "";
    private Dashboard<SampleOrder>? pinned;
    private IReadOnlyList<string> countries = [];

    /// <summary>The dashboard the view renders: the whole thing, or a scope of it per country (concept §4.10). Scopes are kept so switching back is a cache hit.</summary>
    private Dashboard<SampleOrder> Scoped
    {
        get
        {
            if (scope.Length == 0)
            {
                return Dashboard;
            }

            if (scope == PinnedScope)
            {
                return pinned ?? Dashboard;
            }

            if (!scopes.TryGetValue(scope, out Dashboard<SampleOrder>? scoped))
            {
                string country = scope;
                scoped = Dashboard.ScopeTo(x => x.Country == country);
                scopes[scope] = scoped;
            }

            return scoped;
        }
    }

    /// <summary>The option value of the scope made from the current selections.</summary>
    private const string PinnedScope = "*";

    private bool CanPin => !selections.IsEmpty;

    /// <summary>"Make this view my dashboard": the current selections become the scope of a new dashboard, which starts with nothing selected (concept §4.10).</summary>
    private void Pin()
    {
        pinned = Scoped.ScopeTo(selections);
        selections = Selections.Empty;
        scope = PinnedScope;
    }

    /// <summary>The shared dashboard from the cache service; the page shows a loading line until it arrives.</summary>
    private Dashboard<SampleOrder> Dashboard => dashboard ?? throw new InvalidOperationException("The dashboard has not loaded yet.");

    protected override async Task OnInitializedAsync()
    {
        dashboard = await Dashboards.GetAsync();
        countries = ((ValueFacetState)dashboard.Calculate().Facet(FacetKey.Of<SampleOrder>(x => x.Country))).Values
            .Select(v => v.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();
    }
}
