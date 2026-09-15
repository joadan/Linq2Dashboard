using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Linq2Dashboard.Indexing;

namespace Linq2Dashboard.Facets;

/// <summary>
/// Built text facet (concept §5). The one index that is generic and keeps the row array, because
/// matching runs the application's function over the rows rather than reading a column
/// (design §4.1). Nothing is precomputed: the facet has no values to count.
/// </summary>
internal sealed class TextFacetIndex<T> : FacetIndex
{
    private readonly T[] items;
    private readonly Func<T, string, bool> predicate;
    private readonly bool parallel;

    public TextFacetIndex(string key, string name, T[] items, Func<T, string, bool> predicate, bool parallel)
        : base(key, name, FacetKind.Text, items.Length)
    {
        this.items = items;
        this.predicate = predicate;
        this.parallel = parallel;
    }

    /// <summary>
    /// Design §4.1: one predicate call per row. Each 64-row chunk fills one word of the row set, so
    /// chunks can run on separate threads without sharing a word; the parallel-counting option
    /// decides whether they do.
    /// </summary>
    public override RowSet RowsMatching(Selection selection)
    {
        TextSelection text = Expect<TextSelection>(selection);
        if (text.IsEmpty)
        {
            return RowSet.Full(RowCount);
        }

        var words = new ulong[RowSet.WordCount(RowCount)];
        if (parallel && words.Length > 1)
        {
            try
            {
                Parallel.For(0, words.Length, word => words[word] = ScanWord(word, text.Text));
            }
            catch (AggregateException e)
            {
                // Every inner exception comes from the predicate; the first one is what a serial scan would have thrown.
                ExceptionDispatchInfo.Throw(e.InnerExceptions[0]);
            }
        }
        else
        {
            for (int word = 0; word < words.Length; word++)
            {
                words[word] = ScanWord(word, text.Text);
            }
        }

        return RowSet.FromOwnedWords(words, RowCount);
    }

    /// <summary>A text facet has no totals, so the same index serves every scope (concept §4.10); the pipeline intersects its rows with the scope.</summary>
    public override FacetIndex Scope(RowSet scope) => this;

    /// <summary>Nothing to count: the state carries the text and the context size (design §2.4).</summary>
    public override FacetState Present(RowSet context, Selection? selection)
    {
        ExpectOrNull<TextSelection>(selection);
        return new TextFacetState(Key, Name, selection, context.Count);
    }

    /// <summary>Design §2.5: <c>{ "text": "acme" }</c>.</summary>
    public override JsonObject Serialize(Selection selection)
    {
        TextSelection text = Expect<TextSelection>(selection);
        return new JsonObject { ["text"] = text.Text };
    }

    /// <summary>A missing, non-string or blank text is nothing usable and drops the selection.</summary>
    public override Selection? Deserialize(JsonObject json)
    {
        if (!json.TryGetPropertyValue("text", out JsonNode? node)
            || node is not JsonValue value
            || !value.TryGetValue(out string? text))
        {
            return null;
        }

        var selection = new TextSelection(text);
        return selection.IsEmpty ? null : selection;
    }

    private ulong ScanWord(int word, string text)
    {
        int start = word << 6;
        int end = Math.Min(start + 64, RowCount);
        ulong bits = 0;
        for (int row = start; row < end; row++)
        {
            if (predicate(items[row], text))
            {
                bits |= 1UL << (row - start);
            }
        }

        return bits;
    }
}
