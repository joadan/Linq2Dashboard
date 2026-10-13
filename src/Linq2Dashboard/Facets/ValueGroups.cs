namespace Linq2Dashboard.Facets;

/// <summary>
/// The groups of a value facet (concept §6, design §4.4): one group index per value code, −1 for an
/// ungrouped value and for the null code, and the group names in first-seen order. Fixed at build
/// and shared by every scope.
/// </summary>
internal sealed class ValueGroups
{
    private ValueGroups(string[] names, int[] groupOf)
    {
        Names = names;
        GroupOf = groupOf;
    }

    /// <summary>Group names, the first spelling met, in the order their first value was met.</summary>
    public string[] Names { get; }

    /// <summary>Index into <see cref="Names"/> per value code, −1 when the value has no group. Code 0, the null value, is always −1.</summary>
    public int[] GroupOf { get; }

    /// <summary>
    /// Reads one group per distinct value from the row that introduced it (<paramref name="firstRows"/>,
    /// indexed by code − 1). Names compare ignoring case, as string values do by default (design §3.3).
    /// </summary>
    public static ValueGroups Build<T>(T[] items, int[] firstRows, Func<T, string?> group)
    {
        var groupOf = new int[firstRows.Length + 1];
        groupOf[0] = -1;
        var names = new List<string>();
        var indexOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int code = 1; code <= firstRows.Length; code++)
        {
            string? name = group(items[firstRows[code - 1]]);
            if (name is null)
            {
                groupOf[code] = -1;
                continue;
            }

            if (!indexOf.TryGetValue(name, out int index))
            {
                index = names.Count;
                indexOf.Add(name, index);
                names.Add(name);
            }

            groupOf[code] = index;
        }

        return new ValueGroups([.. names], groupOf);
    }
}
