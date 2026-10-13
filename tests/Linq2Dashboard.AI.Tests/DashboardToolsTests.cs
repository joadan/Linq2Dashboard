using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Linq2Dashboard.Tests;
using Microsoft.Extensions.AI;

namespace Linq2Dashboard.AI.Tests;

public class DashboardToolsTests
{
    private static readonly FixedTimeProvider Clock = new(TestData.Instant("2026-03-15T10:00:00Z"));

    // Rows: Country SE, NO, null, SE, DK, se, null, NO; Status Open, Open, Closed, Closed, Pending, Open, Pending, Open.
    private static readonly Order[] Rows = TestData.Orders();

    private static readonly Dashboard<Order> Shared = Dashboard.Create(Rows, b =>
    {
        b.ValueFacet(x => x.Country);
        b.ValueFacet(x => x.Status);
        b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
        b.DateFacet(x => x.OrderDate).Granularity(DateGranularity.Month).TimeZone(TestData.Stockholm);
        b.CountMetric("orders");
        b.SumMetric("revenue", x => x.Amount);
        b.UseTimeProvider(Clock);
    });

    private static DashboardTools<Order> Tools(Func<Selections, Task>? show = null, IReadOnlyCollection<string>? allowed = null) =>
        Shared.ToTools(new DashboardToolOptions<Order>
        {
            Name = "orders",
            Summary = "Test orders.",
            Allowed = allowed,
            Descriptions = new Dictionary<string, string> { ["revenue"] = "Order value in SEK." },
            Show = show,
        });

    /// <summary>The rows a where finds, counted by the query tool.</summary>
    private static int CountWhere(string where)
    {
        JsonObject answer = Tools().Query(where: where);
        Assert.False(answer.ContainsKey("error"), answer.ToJsonString());
        return Count(answer);
    }

    /// <summary>The single number of a one-row answer, such as a count.</summary>
    private static int Count(JsonNode answer) => (int)answer["rows"]![0]![0]!.GetValue<double>();

    [Fact]
    public void Tools_are_named_after_the_dashboard()
    {
        Assert.Equal(["orders_describe", "orders_query"], Tools().Functions.Select(f => f.Name));
        Assert.Equal(["orders_describe", "orders_query", "orders_show"], Tools(show: _ => Task.CompletedTask).Functions.Select(f => f.Name));
    }

    [Fact]
    public void Every_tool_description_opens_with_the_summary()
    {
        Assert.All(Tools(show: _ => Task.CompletedTask).Functions, f => Assert.StartsWith("Test orders. ", f.Description));
    }

    [Theory]
    [InlineData("")]
    [InlineData("orders query")]
    [InlineData("a-name-that-is-far-too-long-for-a-tool-prefix")]
    public void A_name_that_cannot_prefix_a_tool_throws_at_creation(string name)
    {
        Assert.Throws<ArgumentException>(() => Shared.ToTools(new DashboardToolOptions<Order> { Name = name }));
    }

    [Fact]
    public void A_description_of_an_unknown_name_throws_at_creation()
    {
        Assert.ThrowsAny<ArgumentException>(() => Shared.ToTools(new DashboardToolOptions<Order>
        {
            Name = "orders",
            Descriptions = new Dictionary<string, string> { ["Revenue"] = "Typo." },
        }));
    }

    [Fact]
    public void Describe_gives_the_core_description_with_the_where_fields()
    {
        JsonObject description = Tools().Describe();

        Assert.Equal(["Country", "Status", "Amount", "OrderDate"], description["facets"]!.AsArray().Select(f => (string)f!["key"]!));
        Assert.Equal("Order value in SEK.", (string?)description["metrics"]![1]!["description"]);
        Assert.Equal("gridify", (string?)description["where"]!["syntax"]);
        JsonArray fields = description["where"]!["fields"]!.AsArray();
        Assert.Contains(fields, f => (string)f!["name"]! == "Amount" && (string)f["kind"]! == "number");
        Assert.Contains(fields, f => (string)f!["name"]! == "Kind" && (string)f["kind"]! == "text");
        Assert.Contains(fields, f => (string)f!["name"]! == "Due" && (string)f["kind"]! == "date");
        Assert.DoesNotContain(fields, f => (string)f!["name"]! is "Tags" or "Address");
    }

    [Fact]
    public void Allowed_limits_describe_query_and_the_where_alike()
    {
        DashboardTools<Order> tools = Tools(allowed: ["Country", "Amount", "revenue"]);

        JsonObject description = tools.Describe();
        Assert.Equal(["Country", "Amount"], description["facets"]!.AsArray().Select(f => (string)f!["key"]!));
        Assert.Equal(["Amount", "Country"], description["where"]!["fields"]!.AsArray().Select(f => (string)f!["name"]!));

        JsonObject refused = tools.Query(groupBy: ["Status"]);
        Assert.Equal("groupBy[0]", (string?)refused["path"]);

        JsonObject unmapped = tools.Query(where: "Status=Open");
        Assert.Equal("where", (string?)unmapped["path"]);
    }

    [Fact]
    public void Query_assembles_the_parts_into_the_core_query()
    {
        JsonObject answer = Tools().Query(selections: "Status=Open", groupBy: ["Country"], values: ["revenue", "count"], orderBy: "-revenue", take: 1);
        QueryResult expected = Shared.Query(Shared.QueryFromJson(
            """{ "selections": "Status=Open", "groupBy": ["Country"], "values": ["revenue", "count"], "orderBy": "-revenue", "take": 1 }"""));

        Assert.Equal(Shared.Serializer.ToJson(expected), answer.ToJsonString());
        Assert.True((bool)answer["cut"]!);
    }

    [Fact]
    public void Query_without_parts_counts_every_row()
    {
        Assert.Equal(Rows.Length, Count(Tools().Query()));
    }

    [Fact]
    public void Query_errors_come_back_as_results_with_a_path()
    {
        JsonObject answer = Tools().Query(groupBy: ["Contry"]);

        Assert.Equal("groupBy[0]", (string?)answer["path"]);
        Assert.Contains("Country", (string?)answer["error"]);
    }

    [Fact]
    public void A_selection_that_does_not_read_comes_back_with_its_key()
    {
        JsonObject answer = Tools().Query(selections: "Amount=lots");

        Assert.Equal("selections.Amount", (string?)answer["path"]);
    }

    [Theory]
    [InlineData("Country=se", 3)]                       // strings ignore case, as value facets do
    [InlineData("Country=null", 2)]                     // a null string is found by null
    [InlineData("Country=", 2)]                         // and by nothing
    [InlineData("Country!=SE", 5)]                      // null is a value other than SE
    [InlineData("Country=*e", 3)]                       // contains does not throw on null
    [InlineData("Country^N|Country$k", 3)]
    [InlineData("Status=open,Country=no", 2)]
    [InlineData("Amount>99.5", 6)]                      // decimals read the same under any culture
    [InlineData("Discount=null", 3)]
    [InlineData("Quantity>=2", 4)]
    [InlineData("Verified=true", 4)]
    [InlineData("IsActive=false", 3)]
    [InlineData("Kind=store", 3)]                       // enums by name, ignoring case
    [InlineData("OrderDate>=2026-03-01", 5)]
    [InlineData("Shipped>2026-03-01", 3)]
    [InlineData("Shipped=null", 3)]
    [InlineData("Due<2026-03-01", 3)]
    [InlineData("(Amount>=1000|Amount<100),IsActive=true", 2)]
    public void The_where_has_facet_semantics_for_every_type(string where, int expected)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
        try
        {
            Assert.Equal(expected, CountWhere(where));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Theory]
    [InlineData("Amount>abc")]
    [InlineData("Address=Oslo")]
    [InlineData("Nope=1")]
    [InlineData("Amount>>")]
    public void A_where_that_does_not_read_comes_back_with_the_fields(string where)
    {
        JsonObject answer = Tools().Query(where: where);

        Assert.Equal("where", (string?)answer["path"]);
        Assert.Contains("Fields: Amount (number)", (string?)answer["error"]);
    }

    [Fact]
    public void Filterable_chooses_the_where_fields()
    {
        DashboardTools<Order> tools = Shared.ToTools(new DashboardToolOptions<Order> { Name = "orders", Filterable = ["Amount"] });

        Assert.Equal(["Amount"], tools.Describe()["where"]!["fields"]!.AsArray().Select(f => (string)f!["name"]!));
        Assert.Equal("where", (string?)tools.Query(where: "Status=Open")["path"]);
    }

    [Fact]
    public void An_empty_filterable_leaves_the_where_out_of_the_tool()
    {
        DashboardTools<Order> tools = Shared.ToTools(new DashboardToolOptions<Order> { Name = "orders", Filterable = [] });
        AIFunction query = tools.Functions.Single(f => f.Name == "orders_query");

        Assert.False(tools.HasWhere);
        Assert.False(tools.Describe().ContainsKey("where"));
        Assert.False(query.JsonSchema.GetProperty("properties").TryGetProperty("where", out _));
        Assert.True(query.JsonSchema.GetProperty("properties").TryGetProperty("groupBy", out _));
    }

    [Theory]
    [InlineData("Missing")]
    [InlineData("Tags")]
    [InlineData("Address")]
    public void A_filterable_name_the_where_cannot_use_throws_at_creation(string name)
    {
        Assert.Throws<ArgumentException>(() => Shared.ToTools(new DashboardToolOptions<Order> { Name = "orders", Filterable = [name] }));
    }

    [Fact]
    public void A_filterable_name_outside_allowed_throws_at_creation()
    {
        Assert.Throws<ArgumentException>(() => Shared.ToTools(new DashboardToolOptions<Order>
        {
            Name = "orders",
            Allowed = ["Country"],
            Filterable = ["Amount"],
        }));
    }

    [Fact]
    public void The_application_parser_replaces_gridify()
    {
        static Expression<Func<Order, bool>> Parse(string text) =>
            text == "big" ? x => x.Amount >= 1000 : throw new FormatException("Only 'big' is known.");

        DashboardTools<Order> tools = Shared.ToTools(new DashboardToolOptions<Order> { Name = "orders", Where = Parse, WhereHelp = "Say big." });

        Assert.Equal(2, Count(tools.Query(where: "big")));
        Assert.Contains("Say big.", (string?)tools.Query(where: "small")["error"]);
        Assert.Equal("Say big.", (string?)tools.Describe()["where"]!["help"]);
    }

    [Fact]
    public async Task Show_hands_strictly_read_selections_to_the_application()
    {
        Selections? shown = null;
        DashboardTools<Order> tools = Tools(show: s => { shown = s; return Task.CompletedTask; });

        JsonObject result = await tools.ShowAsync("Country=SE,NO&OrderDate=thisYear");

        Assert.Equal(Shared.Serializer.FromQueryString("Country=SE,NO&OrderDate=thisYear"), shown);
        Assert.Equal(Shared.Serializer.ToQueryString(shown!), (string?)result["shown"]);
    }

    [Fact]
    public async Task Show_refuses_an_invented_key_without_calling_the_application()
    {
        bool called = false;
        DashboardTools<Order> tools = Tools(show: _ => { called = true; return Task.CompletedTask; });

        JsonObject result = await tools.ShowAsync("Region=North");

        Assert.False(called);
        Assert.StartsWith("selections", (string?)result["path"]);
    }

    [Fact]
    public async Task Show_refuses_a_facet_outside_allowed()
    {
        bool called = false;
        DashboardTools<Order> tools = Tools(show: _ => { called = true; return Task.CompletedTask; }, allowed: ["Country"]);

        JsonObject result = await tools.ShowAsync("Status=Open");

        Assert.False(called);
        Assert.Equal("selections.Status", (string?)result["path"]);
    }

    [Fact]
    public async Task Show_with_nothing_clears_the_selections()
    {
        Selections? shown = null;
        DashboardTools<Order> tools = Tools(show: s => { shown = s; return Task.CompletedTask; });

        await tools.ShowAsync("");

        Assert.Equal(Selections.Empty, shown);
    }

    [Fact]
    public async Task The_query_function_reads_arguments_as_a_model_sends_them()
    {
        AIFunction query = Tools().Functions.Single(f => f.Name == "orders_query");
        using JsonDocument arguments = JsonDocument.Parse("""{ "groupBy": ["Status"], "values": ["orders"], "orderBy": "-orders", "take": 1 }""");

        object? result = await query.InvokeAsync(new AIFunctionArguments(
            arguments.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.Clone())));

        JsonNode answer = JsonNode.Parse(JsonSerializer.Serialize(result))!;
        Assert.Equal("Open", (string?)answer["rows"]![0]![0]);
        Assert.Equal(4, answer["rows"]![0]![1]!.GetValue<double>());
    }

    [Fact]
    public void The_query_function_has_one_optional_parameter_per_part()
    {
        JsonElement schema = Tools().Functions.Single(f => f.Name == "orders_query").JsonSchema;

        Assert.Equal(
            ["selections", "where", "groupBy", "values", "columns", "orderBy", "take"],
            schema.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        Assert.False(schema.TryGetProperty("required", out JsonElement required) && required.GetArrayLength() > 0);
    }
}
