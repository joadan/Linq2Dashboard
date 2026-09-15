using Linq2Dashboard.SampleData;

namespace Linq2Dashboard.Sample.Components.Pages;

public partial class Home
{
    private readonly Dictionary<string, Dashboard<SampleOrder>> scopes = new(StringComparer.Ordinal);
    private Selections selections = Selections.Empty;
    private string scope = "";
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

            if (!scopes.TryGetValue(scope, out Dashboard<SampleOrder>? scoped))
            {
                string country = scope;
                scoped = Dashboard.Where(x => x.Country == country);
                scopes[scope] = scoped;
            }

            return scoped;
        }
    }

    protected override void OnInitialized() =>
        countries = ((ValueFacetState)Dashboard.Calculate().Facet("Country")).Values
            .Select(v => v.Value)
            .OfType<string>()
            .Order(StringComparer.Ordinal)
            .ToList();
}
