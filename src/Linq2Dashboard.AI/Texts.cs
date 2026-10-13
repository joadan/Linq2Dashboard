namespace Linq2Dashboard.AI;

/// <summary>
/// The tool and parameter descriptions: the agent's manual (design §10.10). Written for a small model: what each tool
/// is for, the syntax by example, and how several queries combine.
/// </summary>
internal static class Texts
{
    public const string Selections =
        "Facet selections as URL query text: Country=SE,NO&Status=Shipped. Values of one facet are OR, facets are AND. " +
        "A range or date facet takes intervals, Amount=[100..500) or Amount=1000.., and a date facet a preset, OrderDate=last30Days. " +
        "null selects the rows without a value. Facet keys and values as in describe; a labelled value may be named by its label.";

    public const string Where =
        "A filter on plain properties in Gridify syntax, combined with the selections by AND: Amount>=1000,Status=Open|Closed. " +
        "Operators = != < > <= >= =* (contains) ^ (starts with) $ (ends with), as in Message=*timeout; there are no wildcards. " +
        "',' is AND, '|' is OR, parentheses group. " +
        "Text ignores case; Field=null finds rows without a value. The fields are under where in describe.";

    public const string GroupBy =
        "Up to two dimensions to group by: a facet key or property name, or a date with a period, OrderDate:Month " +
        "(Day, Week, Month, Quarter, Year). Leave out groupBy and values to list rows with columns instead.";

    public const string Values =
        "Values per group: a metric key, count, or sum:X, avg:X, min:X, max:X, distinct:X over a facet or property. Empty counts rows.";

    public const string Columns =
        "Instead of groupBy and values: list rows, one column per facet key or property name.";

    public const string OrderBy =
        "One key from groupBy, values or columns, written as there; a leading - sorts descending: -revenue, -avg:Amount.";

    public const string Take =
        "How many groups or rows to return. Rows default to 100 and stop at 10000; groups are all returned without it.";

    public const string ShowSelections =
        "The selections to show, as URL query text like the query tool's: Country=SE&OrderDate=thisYear. Empty clears them.";

    public static string Describe(string name, string query) =>
        $"Describes the {name} dashboard: its facets (with their values when few), metrics, properties, the periods a date " +
        $"groups by, the limits, and the fields a where can filter on. Call it once before {query}; names are case-sensitive.";

    public static string Query(string name, string describe) =>
        $"Asks the {name} dashboard one question and returns one table: columns, then rows as arrays in column order. " +
        "Group with groupBy and values, or list rows with columns. total is the number of groups or matching rows; " +
        "when cut is true the answer is not the whole, so tell the user. For a share of a total, a comparison of periods, " +
        "a top N per group or a drill-down, ask several queries: put a cell of one answer into the selections of the next. " +
        $"An error comes back as error and path; correct the query and ask again. Call {describe} first.";

    public static string Show(string name, string query) =>
        $"Shows selections on the user's {name} dashboard, which then filters to them. It changes only what the user sees; " +
        $"to answer a question use {query}. Use it when the user asks to see something.";
}
