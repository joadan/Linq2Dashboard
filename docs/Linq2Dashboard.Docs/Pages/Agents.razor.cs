using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Linq2Dashboard.AI;
using Linq2Dashboard.SampleData;

namespace Linq2Dashboard.Docs.Pages;

/// <summary>The AI package's guide, with a panel that runs the query tool over the demo's data in the browser.</summary>
public partial class Agents
{
    private const int Rows = 20_000;

    private const string PackageExample = """
        dotnet add package Linq2Dashboard.AI --prerelease
        dotnet add package Microsoft.Extensions.AI
        """;

    private const string ToolsExample = """
        using Linq2Dashboard.AI;

        Dashboard<Order> dashboard = await Dashboards.GetAsync();

        DashboardTools<Order> tools = dashboard.ToTools(new DashboardToolOptions<Order>
        {
            Name = "orders",                                         // orders_describe, orders_query, orders_show
            Summary = "The shop's orders, one row per order.",
            Allowed = ["Country", "Status", "Amount", "OrderDate", "orders", "revenue"],
            Descriptions = new Dictionary<string, string>
            {
                ["Status"] = "Pending until the order ships, then Shipped; Cancelled orders never ship.",
                ["Amount"] = "The order value in EUR, excluding VAT.",
            },
        });
        """;

    private const string ChatExample = """
        using Microsoft.Extensions.AI;

        IChatClient chat = new ChatClientBuilder(providerClient)   // the IChatClient of your model provider
            .UseFunctionInvocation()                                // calls the tools the model asks for
            .Build();

        List<ChatMessage> messages =
        [
            new(ChatRole.System, "You answer questions about the shop's orders with the orders tools. Never guess a number."),
            new(ChatRole.User, "Which country had the most revenue this year, and how much of it has shipped?"),
        ];

        ChatResponse response = await chat.GetResponseAsync(messages, new ChatOptions { Tools = [.. tools.Functions] });
        """;

    private const string ShowExample = """
        <DashboardView Context="dash" Dashboard="dashboard" @bind-Selections="selections" SyncUrl="true">
            ...
        </DashboardView>

        @code {
            private Selections selections = Selections.Empty;

            private DashboardTools<Order> CreateTools(Dashboard<Order> dashboard) => dashboard.ToTools(new DashboardToolOptions<Order>
            {
                Name = "orders",
                // ... Summary, Allowed and Descriptions as above ...
                Show = shown => InvokeAsync(() =>
                {
                    selections = shown;                           // the view follows, and the URL with it
                    StateHasChanged();
                }),
            });
        }
        """;

    private static readonly Example[] Examples =
    [
        new("Revenue per country", GroupBy: "Country", Values: "revenue, orders", OrderBy: "-revenue", Take: "5"),
        new("Shipped per quarter", Selections: "Status=Shipped", GroupBy: "OrderDate:Quarter", Values: "orders, revenue, avg:Amount"),
        new("Largest E-Com orders", Where: "Channel=E-Com,Amount>=1000", Columns: "Id, Customer, Amount, OrderDate", OrderBy: "-Amount", Take: "5"),
        new("Channel by status", Selections: "Country=Sweden,Norway", GroupBy: "Channel, Status", Values: "revenue", OrderBy: "-revenue", Take: "8"),
        new("A typo", GroupBy: "Contry", Values: "revenue"),
    ];

    /// <summary>Writes <c>&gt;</c> and <c>&amp;</c> as they are: the text is shown, not embedded in HTML.</summary>
    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private DashboardTools<SampleOrder>? tools;
    private string selectionsText = "";
    private string whereText = "";
    private string groupByText = "";
    private string valuesText = "";
    private string columnsText = "";
    private string orderByText = "";
    private string takeText = "";
    private string? call;
    private string? result;
    private long? elapsed;
    private int runs; // shown with the result, so a run that returns the same answer still visibly ran

    protected override async Task OnInitializedAsync()
    {
        await Task.Delay(1); // let the page paint before the single-threaded build
        Dashboard<SampleOrder> dashboard = SampleOrders.BuildDashboard(Rows);
        tools = dashboard.ToTools(new DashboardToolOptions<SampleOrder>
        {
            Name = "orders",
            Summary = "Generated orders over the last two years, one row per order.",
            Descriptions = new Dictionary<string, string>
            {
                [SampleOrderKeys.Customer] = "The customer's number; the label is the customer's name.",
                [SampleOrderKeys.Revenue] = "The sum of order amounts.",
            },
        });
        Load(Examples[0]);
    }

    private void Load(Example example)
    {
        selectionsText = example.Selections ?? "";
        whereText = example.Where ?? "";
        groupByText = example.GroupBy ?? "";
        valuesText = example.Values ?? "";
        columnsText = example.Columns ?? "";
        orderByText = example.OrderBy ?? "";
        takeText = example.Take ?? "";
        RunQuery();
    }

    private void RunQuery()
    {
        if (tools is null)
        {
            return;
        }

        string? selections = Text(selectionsText);
        string? where = Text(whereText);
        string[]? groupBy = List(groupByText);
        string[]? values = List(valuesText);
        string[]? columns = List(columnsText);
        string? orderBy = Text(orderByText);
        int? take = int.TryParse(takeText, out int number) ? number : null;

        var arguments = new JsonObject();
        AddArgument(arguments, "selections", selections);
        AddArgument(arguments, "where", where);
        AddArgument(arguments, "groupBy", groupBy);
        AddArgument(arguments, "values", values);
        AddArgument(arguments, "columns", columns);
        AddArgument(arguments, "orderBy", orderBy);
        if (take is int count)
        {
            arguments["take"] = count;
        }

        var watch = Stopwatch.StartNew();
        JsonObject answer = tools.Query(selections, where, groupBy, values, columns, orderBy, take);
        elapsed = watch.ElapsedMilliseconds;
        runs++;
        call = $"{tools.QueryName}({arguments.ToJsonString(Relaxed)})";
        result = Pretty(answer);
    }

    private void RunDescribe()
    {
        if (tools is null)
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        JsonObject description = tools.Describe();
        elapsed = watch.ElapsedMilliseconds;
        runs++;
        call = $"{tools.DescribeName}()";
        result = Pretty(description);
    }

    private static string? Text(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string[]? List(string text)
    {
        string[] items = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items.Length == 0 ? null : items;
    }

    private static void AddArgument(JsonObject arguments, string name, string? text)
    {
        if (text is not null)
        {
            arguments[name] = text;
        }
    }

    private static void AddArgument(JsonObject arguments, string name, string[]? items)
    {
        if (items is not null)
        {
            arguments[name] = new JsonArray(items.Select(i => (JsonNode?)i).ToArray());
        }
    }

    /// <summary>One line per top-level member and per array element: readable, and rows stay rows.</summary>
    private static string Pretty(JsonObject json)
    {
        var text = new StringBuilder("{\n");
        int i = 0;
        foreach ((string name, JsonNode? value) in json)
        {
            text.Append("  \"").Append(name).Append("\": ");
            if (value is JsonArray array && array.Count > 0)
            {
                text.Append("[\n");
                for (int j = 0; j < array.Count; j++)
                {
                    text.Append("    ").Append(array[j]?.ToJsonString(Relaxed) ?? "null").Append(j < array.Count - 1 ? ",\n" : "\n");
                }

                text.Append("  ]");
            }
            else
            {
                text.Append(value?.ToJsonString(Relaxed) ?? "null");
            }

            text.Append(++i < json.Count ? ",\n" : "\n");
        }

        return text.Append('}').ToString();
    }

    private sealed record Example(
        string Title,
        string? Selections = null,
        string? Where = null,
        string? GroupBy = null,
        string? Values = null,
        string? Columns = null,
        string? OrderBy = null,
        string? Take = null);
}
