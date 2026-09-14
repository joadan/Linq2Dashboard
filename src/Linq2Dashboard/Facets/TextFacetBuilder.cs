using Linq2Dashboard.Facets;

namespace Linq2Dashboard;

/// <summary>Fluent configuration of a text facet (design §2.1). The matching semantics live in the function, so the title is the only option.</summary>
public sealed class TextFacetBuilder<T>
{
    private readonly TextFacetDefinition<T> definition;
    private readonly Action ensureMutable;

    internal TextFacetBuilder(TextFacetDefinition<T> definition, Action ensureMutable)
    {
        this.definition = definition;
        this.ensureMutable = ensureMutable;
    }

    public string Key => definition.Key;

    /// <summary>Display name. Defaults to the key.</summary>
    public TextFacetBuilder<T> Title(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ensureMutable();
        definition.Title = title;
        return this;
    }
}
