namespace Linq2Dashboard.Facets;

/// <summary>Configuration of a text facet: the key and the application's matching function (concept §5).</summary>
internal sealed class TextFacetDefinition<T> : FacetDefinition<T>
{
    private readonly Func<T, string, bool> predicate;

    public TextFacetDefinition(string key, Func<T, string, bool> predicate)
        : base(key, FacetKind.Text)
    {
        this.predicate = predicate;
    }

    public override FacetIndex Build(T[] items, TimeProvider timeProvider, bool parallel) =>
        new TextFacetIndex<T>(Key, Name, items, predicate, parallel);
}
