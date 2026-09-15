using Linq2Dashboard.Facets;

namespace Linq2Dashboard;

/// <summary>Fluent configuration of a text facet (design §2.1). The matching semantics live in the function, so the name is the only option. Each new text runs the function over every row, so see the remarks on <see cref="DashboardBuilder{T}.TextFacet"/> before choosing one.</summary>
public sealed class TextFacetBuilder<T>
{
    private readonly TextFacetDefinition<T> definition;
    private readonly Action ensureMutable;

    internal TextFacetBuilder(TextFacetDefinition<T> definition, Action ensureMutable)
    {
        this.definition = definition;
        this.ensureMutable = ensureMutable;
    }

    /// <summary>The facet key, used in selections, state and the Blazor components.</summary>
    public string Key => definition.Key;

    /// <summary>Display name. Defaults to the key.</summary>
    public TextFacetBuilder<T> Name(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ensureMutable();
        definition.Name = name;
        return this;
    }
}
