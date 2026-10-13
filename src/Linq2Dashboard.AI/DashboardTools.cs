using System.ComponentModel;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace Linq2Dashboard.AI;

/// <summary>
/// One dashboard as tools an agent can call (design §10.10): describe what can be asked, query it, and show selections
/// on the user's screen. Each tool is a method returning JSON, for tests and hosts of their own, and an
/// <see cref="AIFunction"/> in <see cref="Functions"/>. Errors in what the agent asked come back as
/// <c>{ "error", "path" }</c> results, never as exceptions, so the agent can correct itself. Thread-safe; holds no
/// conversation state.
/// </summary>
/// <typeparam name="T">The dashboard's row type.</typeparam>
public sealed partial class DashboardTools<T>
{
    private readonly Dashboard<T> dashboard;
    private readonly DashboardToolOptions<T> options;
    private readonly DescribeOptions describeOptions;
    private readonly QueryJsonOptions<T> queryOptions;
    private readonly GridifyWhere<T>? gridify;
    private readonly string? whereHelp;

    internal DashboardTools(Dashboard<T> dashboard, DashboardToolOptions<T> options)
    {
        if (!ToolName().IsMatch(options.Name))
        {
            throw new ArgumentException($"Name '{options.Name}' is not a tool name prefix: use letters, digits, '_' and '-', at most 40 characters.", nameof(options));
        }

        this.dashboard = dashboard;
        this.options = options;
        describeOptions = new DescribeOptions { Allowed = options.Allowed, Descriptions = options.Descriptions, ValueLimit = options.ValueLimit };
        dashboard.Describe(describeOptions);   // checks the descriptions' names now rather than at the first call

        Func<string, Expression<Func<T, bool>>>? where = options.Where;
        whereHelp = options.WhereHelp;
        if (where is null)
        {
            gridify = GridifyWhere<T>.Create(options.Filterable, options.Allowed);
            where = gridify is null ? null : gridify.Parse;
            whereHelp = gridify?.Help;
        }

        queryOptions = new QueryJsonOptions<T> { Where = where, WhereHelp = whereHelp, Allowed = options.Allowed };
        HasWhere = where is not null;
        Functions = CreateFunctions();
    }

    /// <summary>The describe tool's name, <c>{Name}_describe</c>.</summary>
    public string DescribeName => $"{options.Name}_describe";

    /// <summary>The query tool's name, <c>{Name}_query</c>.</summary>
    public string QueryName => $"{options.Name}_query";

    /// <summary>The show tool's name, <c>{Name}_show</c>; there is a show tool only when the options give <see cref="DashboardToolOptions{T}.Show"/>.</summary>
    public string ShowName => $"{options.Name}_show";

    /// <summary>True when the query tool takes a where: a parser was given, or some allowed property can be filtered.</summary>
    public bool HasWhere { get; }

    /// <summary>The tools for the agent: describe, query and, with <see cref="DashboardToolOptions{T}.Show"/>, show.</summary>
    public IReadOnlyList<AIFunction> Functions { get; }

    /// <summary>
    /// What the agent can ask (design §10.7): the facets, metrics and properties in <see cref="DashboardToolOptions{T}.Allowed"/>
    /// with the application's descriptions, the periods and limits, and under <c>where</c> what the where can filter on.
    /// </summary>
    public JsonObject Describe()
    {
        JsonObject description = dashboard.Serializer.ToJsonObject(dashboard.Describe(describeOptions));
        if (gridify is not null)
        {
            description["where"] = new JsonObject
            {
                ["syntax"] = "gridify",
                ["fields"] = new JsonArray(gridify.Fields
                    .Select(f => (JsonNode)new JsonObject { ["name"] = f.Name, ["kind"] = GridifyWhere<T>.Kind(f.Kind) })
                    .ToArray()),
            };
        }
        else if (HasWhere)
        {
            description["where"] = new JsonObject { ["help"] = whereHelp };
        }

        return description;
    }

    /// <summary>
    /// Asks one question and returns one table (design §10.6): a column header, rows as arrays, <c>total</c> and
    /// <c>cut</c>. The parts are the query's JSON parts in their short text forms; a part left null is not asked.
    /// </summary>
    public JsonObject Query(
        [Description(Texts.Selections)] string? selections = null,
        [Description(Texts.Where)] string? where = null,
        [Description(Texts.GroupBy)] string[]? groupBy = null,
        [Description(Texts.Values)] string[]? values = null,
        [Description(Texts.Columns)] string[]? columns = null,
        [Description(Texts.OrderBy)] string? orderBy = null,
        [Description(Texts.Take)] int? take = null,
        CancellationToken cancellationToken = default)
    {
        var json = new JsonObject();
        AddText(json, "selections", selections);
        AddText(json, "where", where);
        AddList(json, "groupBy", groupBy);
        AddList(json, "values", values);
        AddList(json, "columns", columns);
        AddText(json, "orderBy", orderBy);
        if (take is int count)
        {
            json["take"] = count;
        }

        try
        {
            DashboardQuery<T> query = dashboard.QueryFromJsonObject(json, queryOptions);
            return dashboard.Serializer.ToJsonObject(dashboard.Query(query, cancellationToken));
        }
        catch (DashboardQueryException e)
        {
            return Error(e);
        }
    }

    /// <summary>
    /// Shows <paramref name="selections"/>, in the query-string form, on the user's screen through
    /// <see cref="DashboardToolOptions{T}.Show"/>; empty clears them. They are read strictly and checked against
    /// <see cref="DashboardToolOptions{T}.Allowed"/> first, so an invented key is an error rather than a silent no-op.
    /// </summary>
    public async Task<JsonObject> ShowAsync([Description(Texts.ShowSelections)] string? selections = null)
    {
        Func<Selections, Task> show = options.Show
            ?? throw new InvalidOperationException("There is no show tool: give DashboardToolOptions.Show.");

        Selections read;
        try
        {
            read = string.IsNullOrWhiteSpace(selections)
                ? Selections.Empty
                : dashboard.QueryFromJsonObject(new JsonObject { ["selections"] = selections }, queryOptions).Selections;
        }
        catch (DashboardQueryException e)
        {
            return Error(e);
        }

        await show(read);
        return new JsonObject { ["shown"] = dashboard.Serializer.ToQueryString(read) };
    }

    private List<AIFunction> CreateFunctions()
    {
        string summary = string.IsNullOrWhiteSpace(options.Summary) ? string.Empty : options.Summary.Trim() + " ";
        List<AIFunction> functions =
        [
            AIFunctionFactory.Create(Describe, new AIFunctionFactoryOptions
            {
                Name = DescribeName,
                Description = summary + Texts.Describe(options.Name, QueryName),
            }),
            AIFunctionFactory.Create(
                typeof(DashboardTools<T>).GetMethod(HasWhere ? nameof(Query) : nameof(QueryWithoutWhere), BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!,
                this,
                new AIFunctionFactoryOptions
                {
                    Name = QueryName,
                    Description = summary + Texts.Query(options.Name, DescribeName),
                }),
        ];

        if (options.Show is not null)
        {
            functions.Add(AIFunctionFactory.Create(ShowAsync, new AIFunctionFactoryOptions
            {
                Name = ShowName,
                Description = summary + Texts.Show(options.Name, QueryName),
            }));
        }

        return functions;
    }

    /// <summary>The query tool when nothing can be filtered: the same parameters without the where.</summary>
    private JsonObject QueryWithoutWhere(
        [Description(Texts.Selections)] string? selections = null,
        [Description(Texts.GroupBy)] string[]? groupBy = null,
        [Description(Texts.Values)] string[]? values = null,
        [Description(Texts.Columns)] string[]? columns = null,
        [Description(Texts.OrderBy)] string? orderBy = null,
        [Description(Texts.Take)] int? take = null,
        CancellationToken cancellationToken = default) =>
        Query(selections, null, groupBy, values, columns, orderBy, take, cancellationToken);

    private static void AddText(JsonObject json, string part, string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            json[part] = text;
        }
    }

    private static void AddList(JsonObject json, string part, string[]? items)
    {
        if (items is { Length: > 0 })
        {
            json[part] = new JsonArray(items.Select(i => (JsonNode?)i).ToArray());
        }
    }

    private static JsonObject Error(DashboardQueryException e) => new() { ["error"] = e.Message, ["path"] = e.Path };

    [GeneratedRegex("^[A-Za-z0-9_-]{1,40}$")]
    private static partial Regex ToolName();
}
