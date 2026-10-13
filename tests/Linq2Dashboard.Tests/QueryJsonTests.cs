using System.Linq.Expressions;

namespace Linq2Dashboard.Tests;

public class QueryJsonTests
{
    private static readonly FixedTimeProvider Clock = new(TestData.Instant("2026-03-15T10:00:00Z"));

    // Rows as in QueryTests: Country SE, NO, null, SE, DK, se, null, NO; Amount 100, 250, 500, 999.5, 1000, 2500, 0, 75.
    private static readonly Dashboard<Order> Shared = Dashboard.Create(TestData.Orders(), b =>
    {
        b.ValueFacet(x => x.Country);
        b.ValueFacet(x => x.Status);
        b.ValueFacet(x => x.Quantity);
        b.ValueFacet("customer", x => x.Id).Label(x => $"Order {x.Id}");
        b.MultiValueFacet(x => x.Tags);
        b.RangeFacet(x => x.Amount).Buckets(100, 500, 1000);
        b.DateFacet(x => x.OrderDate).Granularity(DateGranularity.Month).TimeZone(TestData.Stockholm);
        b.CountMetric("orders");
        b.SumMetric("revenue", x => x.Amount);
        b.UseTimeProvider(Clock);
    });

    /// <summary>A stand-in for a where parser such as Gridify: one form, and the kind of message a parser gives otherwise.</summary>
    private static Expression<Func<Order, bool>> ParseWhere(string text) =>
        text == "Amount>=500" ? x => x.Amount >= 500 : throw new FormatException("Argument types do not match");

    private static readonly QueryJsonOptions<Order> WithWhere = new() { Where = ParseWhere, WhereHelp = "Fields: Amount, Status." };

    private static string AnswerJson(DashboardQuery<Order> query) => Shared.Serializer.ToJson(Shared.Query(query));

    [Fact]
    public void Answer_json_has_a_column_header_and_rows_as_arrays()
    {
        string json = AnswerJson(new()
        {
            GroupBy = [Dimension.Of("Country")],
            Values = [QueryValue.Metric("revenue")],
            OrderBy = QueryOrder.Descending("revenue"),
            Take = 2,
        });

        Assert.Equal(
            """{"columns":[{"key":"Country","name":"Country","role":"dimension","kind":"value","source":"Country"},{"key":"revenue","name":"revenue","role":"value","kind":"number","source":"revenue","aggregation":"sum"}],"rows":[["SE",3599.5],["DK",1000]],"total":4,"cut":true}""",
            json);
    }

    [Fact]
    public void Answer_json_writes_intervals_in_url_notation_and_periods_as_dates()
    {
        var amounts = Shared.Query(new() { GroupBy = [Dimension.Of("Amount")] });
        var first = (RangeInterval)amounts.Rows[0][0]!;
        string url = Shared.Serializer.ToQueryString(Selections.Empty.ToggleInterval("Amount", first));

        Assert.Contains($"[\"{url["Amount=".Length..]}\",2]", Shared.Serializer.ToJson(amounts));
        Assert.Contains("""["2026-01-01",1]""", AnswerJson(new() { GroupBy = [Dimension.Of("OrderDate")] }));
        Assert.Contains("""["2026-01-01",6]""", AnswerJson(new() { GroupBy = [Dimension.Of("OrderDate", DateGranularity.Quarter)] }));
    }

    [Fact]
    public void Answer_json_writes_labels_lists_instants_and_dates()
    {
        string json = AnswerJson(new()
        {
            Columns = [Dimension.Of("customer"), Dimension.Of("Tags"), Dimension.Of("OrderDate"), Dimension.Of("Due"), Dimension.Of("Shipped")],
            Take = 2,
        });

        Assert.Contains("""{"key":"Due","name":"Due","role":"dimension","kind":"date","source":"Due"}""", json);
        Assert.Contains("""[{"value":1,"label":"Order 1"},["urgent","gift"],"2026-01-15T10:00+01:00","2026-01-20","2026-01-16T08:00Z"]""", json);
        Assert.Contains("""[{"value":2,"label":"Order 2"},["gift"],"2026-02-03T09:30+01:00","2026-02-10",null]""", json);
    }

    [Fact]
    public void A_dateonly_property_column_stays_a_date()
    {
        var answer = Shared.Query(new() { Columns = [Dimension.Of("Due")], Take = 1 });

        Assert.Equal(QueryColumnKind.Date, answer.Column("Due").Kind);
        Assert.Equal(new DateOnly(2026, 1, 20), answer.Rows[0]["Due"]);
    }

    [Fact]
    public void Query_from_json_reads_the_short_text_forms()
    {
        var query = Shared.QueryFromJson("""
            {
              "selections": "Status=Open,Closed",
              "groupBy": ["Country", "OrderDate:Quarter"],
              "values": ["revenue", "count", "avg:Amount", "distinct:Tags"],
              "orderBy": "-avg:Amount",
              "take": 3
            }
            """);

        Assert.Equal([Dimension.Of("Country"), Dimension.Of("OrderDate", DateGranularity.Quarter)], query.GroupBy);
        Assert.Equal([QueryValue.Metric("revenue"), QueryValue.Count(), QueryValue.Average("Amount"), QueryValue.Distinct("Tags")], query.Values);
        Assert.Equal(QueryOrder.Descending("Average(Amount)"), query.OrderBy);
        Assert.Equal(3, query.Take);
        Assert.Equal(Selections.Empty.With("Status", ValueSelection.Of("Open", "Closed")), query.Selections);
    }

    [Fact]
    public void Query_from_json_reads_the_object_forms_to_the_same_parts()
    {
        var text = Shared.QueryFromJson("""{ "groupBy": ["OrderDate:Month"], "values": ["sum:Amount"], "orderBy": "OrderDate:Month" }""");
        var objects = Shared.QueryFromJson("""
            {
              "groupBy": [{ "name": "OrderDate", "period": "month" }],
              "values": [{ "aggregation": "sum", "of": "Amount" }],
              "orderBy": { "key": "Month(OrderDate)", "descending": false }
            }
            """);

        Assert.Equal(text.GroupBy, objects.GroupBy);
        Assert.Equal(text.Values, objects.Values);
        Assert.Equal(text.OrderBy, objects.OrderBy);
    }

    [Fact]
    public void Query_from_json_reads_renames_properties_and_metrics_in_object_form()
    {
        var query = Shared.QueryFromJson("""
            {
              "groupBy": [{ "property": "Country", "as": "land" }],
              "values": [{ "metric": "orders", "as": "n" }],
              "orderBy": "-n"
            }
            """);

        Assert.Equal([Dimension.Property("Country").As("land")], query.GroupBy);
        Assert.Equal([QueryValue.Metric("orders").As("n")], query.Values);
        Assert.Equal([("SE", 3.0), ("NO", 2.0), (null, 2.0), ("DK", 1.0)], Shared.Query(query).Rows.Select(r => ((r[0] as QueryValueCell)?.Value, (double)r[1]!)));
    }

    [Fact]
    public void Query_from_json_answers_like_the_typed_query()
    {
        var fromJson = Shared.QueryFromJson("""{ "selections": { "Status": { "values": ["Open"] } }, "where": "Amount>=500", "columns": ["Id", "Amount"] }""", WithWhere);

        var answer = Shared.Query(fromJson);

        Assert.Equal([(6, 2500.0)], answer.Rows.Select(r => ((int)((QueryValueCell)r[0]!).Value!, (double)r[1]!)));
    }

    [Fact]
    public void Strict_query_string_selections_refuse_what_the_lenient_reader_drops()
    {
        Assert.Equal(Selections.Empty.With("Quantity", ValueSelection.Of(2)), Shared.Serializer.FromQueryString("Quantity=2,x"));

        var error = Assert.Throws<SelectionFormatException>(() => Shared.Serializer.FromQueryString("Quantity=2,x", "", strict: true));
        Assert.Contains("'x' is not a value of facet 'Quantity'", error.Message);
        Assert.IsAssignableFrom<FormatException>(error);
    }

    [Fact]
    public void Strict_json_selections_refuse_what_the_lenient_reader_drops()
    {
        Assert.Equal(Selections.Empty.With("Quantity", ValueSelection.Of(2)), Shared.Serializer.FromJson("""{ "Quantity": { "values": [2, "x"] } }"""));

        Assert.Throws<SelectionFormatException>(() => Shared.Serializer.FromJson("""{ "Quantity": { "values": [2, "x"] } }""", strict: true));
        Assert.Throws<SelectionFormatException>(() => Shared.Serializer.FromJson("""{ "Region": { "values": ["North"] } }""", strict: true));
        Assert.Throws<SelectionFormatException>(() => Shared.Serializer.FromJson("""{ "Amount": { "intervals": [{ "from": 1 }, { "from": "x" }] } }""", strict: true));
        Assert.Equal(
            Shared.Serializer.FromJson("""{ "Amount": { "intervals": [{}] } }"""),
            Shared.Serializer.FromJson("""{ "Amount": { "intervals": [{}] } }""", strict: true));
    }

    [Theory]
    [MemberData(nameof(MistakeNames))]
    public void Query_from_json_is_strict_and_names_the_part(string mistake)
    {
        (string json, string expected, string? path) = Mistakes[mistake];

        var error = Assert.Throws<DashboardQueryException>(() => Shared.QueryFromJson(json, Restricted));
        Assert.Contains(expected, error.Message);
        Assert.Equal(path, error.Path);
    }

    private static readonly QueryJsonOptions<Order> Restricted = new()
    {
        Where = ParseWhere,
        WhereHelp = "Fields: Amount, Status.",
        Allowed = ["Country", "Status", "OrderDate", "Amount", "Tags", "Id", "Quantity", "revenue", "orders"],
    };

    public static TheoryData<string> MistakeNames() => new(Mistakes.Keys);

    private static readonly Dictionary<string, (string Json, string Expected, string? Path)> Mistakes = new()
    {
        ["not json"] = ("{ groupBy: ", "The query is not valid JSON", null),
        ["not an object"] = ("[]", "A query is a JSON object", null),
        ["unknown part"] = ("""{ "groupby": ["Country"] }""", "A query has no 'groupby'. Did you mean 'groupBy'?", "groupby"),
        ["unknown dimension"] = ("""{ "groupBy": ["Status", "Contry"] }""", "Did you mean 'Country'?", "groupBy[1]"),
        ["bad period"] = ("""{ "groupBy": [{ "name": "OrderDate", "period": "fortnight" }] }""", "'fortnight' is not a period", "groupBy[0]"),
        ["dimension with name and property"] = ("""{ "groupBy": [{ "name": "Country", "property": "Country" }] }""", "not both", "groupBy[0]"),
        ["unknown dimension property"] = ("""{ "groupBy": [{ "nam": "Country" }] }""", "This part has no 'nam'. Did you mean 'name'?", "groupBy[0]"),
        ["misspelt metric"] = ("""{ "values": ["revnue"] }""", "Did you mean 'revenue'?", "values[0]"),
        ["count with a name"] = ("""{ "values": ["count:Amount"] }""", "A count counts rows and takes no name", "values[0]"),
        ["aggregation without a name"] = ("""{ "values": [{ "aggregation": "sum" }] }""", "Sum needs the name of what it aggregates", "values[0]"),
        ["order by an unknown key"] = ("""{ "groupBy": ["Country"], "orderBy": "-revenue" }""", "orders by 'revenue', which is not one of its columns", "orderBy"),
        ["take as text"] = ("""{ "take": "ten" }""", "Take is a whole number", "take"),
        ["take above the ceiling"] = ("""{ "columns": ["Id"], "take": 20000 }""", "at most 10000 rows", "take"),
        ["unknown selection"] = ("""{ "selections": "Region=North" }""", "'Region' is not a facet of this dashboard", "selections.Region"),
        ["unreadable selection value"] = ("""{ "selections": "Amount=abc" }""", "'abc' is not a value of facet 'Amount'", "selections.Amount"),
        ["selection named twice"] = ("""{ "selections": "Status=Open&Status=Closed" }""", "named twice", "selections.Status"),
        ["unknown selection shape"] = ("""{ "selections": { "Status": { "value": ["Open"] } } }""", "has no 'value'", "selections.Status"),
        ["where the parser cannot read"] = ("""{ "where": "Amount>abc" }""", "The where 'Amount>abc' cannot be read: Argument types do not match. Fields: Amount, Status.", "where"),
        ["name not allowed"] = ("""{ "values": ["sum:Discount"] }""", "'Discount' is not available here", "values[0]"),
        ["selection not allowed"] = ("""{ "selections": "customer=1" }""", "'customer' is not available here", "selections.customer"),
    };

    [Fact]
    public void Query_from_json_refuses_a_where_without_a_parser()
    {
        var error = Assert.Throws<DashboardQueryException>(() => Shared.QueryFromJson("""{ "where": "Amount>=500" }"""));

        Assert.Equal("where", error.Path);
        Assert.Contains("takes no where", error.Message);
    }
}
