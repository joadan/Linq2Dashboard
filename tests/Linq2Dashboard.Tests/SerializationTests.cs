using System.Text.Json;
using System.Text.Json.Nodes;

namespace Linq2Dashboard.Tests;

public class SerializationTests
{
    private static readonly Dashboard<Order> Shared = Dashboard.Create(TestData.Orders(), b =>
    {
        b.ValueFacet(x => x.Country);
        b.ValueFacet(x => x.Quantity);
        b.ValueFacet(x => x.Kind);
        b.BooleanFacet(x => x.IsActive);
        b.DateFacet("dueDate", x => x.Due).Granularity(DateGranularity.Month);
        b.ValueFacet("due", x => x.Due);
        b.ValueFacet("shipped", x => x.Shipped);
        b.RangeFacet(x => x.Amount);
        b.DateFacet(x => x.OrderDate).Granularity(DateGranularity.Month).TimeZone(TestData.Stockholm);
        b.ValueFacet("city", x => x.Address).Serialize(a => a!.City, s => new Address(s));
    });

    private static SelectionSerializer Serializer => Shared.Serializer;

    private static JsonObject Parse(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public void Empty_selections_serialise_to_an_empty_object()
    {
        Assert.Equal("{}", Serializer.ToJson(Selections.Empty));
        Assert.Equal(Selections.Empty, Serializer.FromJson("{}"));
    }

    [Fact]
    public void Value_selections_write_values_as_json_primitives_with_null_as_null()
    {
        var selections = Selections.Empty
            .With("Country", ValueSelection.Of("SE", null))
            .With("Quantity", ValueSelection.Of(2, 5))
            .With("Kind", ValueSelection.Of(OrderKind.Store))
            .With("IsActive", ValueSelection.Of(true, null));

        JsonObject json = Parse(Serializer.ToJson(selections));

        Assert.Equal("""["SE",null]""", json["Country"]!["values"]!.ToJsonString());
        Assert.Equal("[2,5]", json["Quantity"]!["values"]!.ToJsonString());
        Assert.Equal("""["Store"]""", json["Kind"]!["values"]!.ToJsonString());
        Assert.Equal("[true,null]", json["IsActive"]!["values"]!.ToJsonString());
    }

    [Fact]
    public void Value_selections_round_trip_with_the_facets_value_types()
    {
        var selections = Selections.Empty
            .With("Country", ValueSelection.Of("SE", null))
            .With("Quantity", ValueSelection.Of(2, 5))
            .With("Kind", ValueSelection.Of(OrderKind.Store))
            .With("IsActive", ValueSelection.Of(true, null))
            .With("due", ValueSelection.Of(new DateOnly(2026, 3, 5)))
            .With("shipped", ValueSelection.Of(TestData.Instant("2026-01-16T08:00:00Z"), null));

        Selections restored = Serializer.FromJson(Serializer.ToJson(selections));

        Assert.Equal(selections, restored);
        Assert.IsType<int>(((ValueSelection)restored["Quantity"]!).Values[0]);
        Assert.IsType<OrderKind>(((ValueSelection)restored["Kind"]!).Values[0]);
        Assert.IsType<DateOnly>(((ValueSelection)restored["due"]!).Values[0]);
        Assert.IsType<DateTimeOffset>(((ValueSelection)restored["shipped"]!).Values[0]);
    }

    [Fact]
    public void Restored_selections_select_the_same_rows()
    {
        var selections = Selections.Empty
            .With("Country", ValueSelection.Of("se"))
            .With("Quantity", ValueSelection.Of(5, null))
            .With("Amount", RangeSelection.Between(100, 1000))
            .With("OrderDate", DateSelection.Relative(DatePreset.ThisYear));

        Selections restored = Serializer.FromJson(Serializer.ToJson(selections));

        Assert.Equal(Shared.Calculate(selections).Items.Select(o => o.Id), Shared.Calculate(restored).Items.Select(o => o.Id));
    }

    [Fact]
    public void Range_selections_omit_defaults_and_round_trip()
    {
        var closed = Selections.Empty.With("Amount", RangeSelection.Between(100, 500));
        var halfOpen = Selections.Empty.With("Amount", new RangeSelection(100, 500, toInclusive: false, includeNull: true));
        var open = Selections.Empty.With("Amount", RangeSelection.AtLeast(500));
        var onlyNull = Selections.Empty.With("Amount", RangeSelection.OnlyNull);

        Assert.Equal("""{"Amount":{"from":100,"to":500}}""", Serializer.ToJson(closed));
        Assert.Equal("""{"Amount":{"from":100,"to":500,"toInclusive":false,"includeNull":true}}""", Serializer.ToJson(halfOpen));
        Assert.Equal("""{"Amount":{"from":500}}""", Serializer.ToJson(open));
        Assert.Equal("""{"Amount":{"onlyNull":true}}""", Serializer.ToJson(onlyNull));

        foreach (Selections selections in new[] { closed, halfOpen, open, onlyNull })
        {
            Assert.Equal(selections, Serializer.FromJson(Serializer.ToJson(selections)));
        }
    }

    /// <summary>Design §2.5: several intervals write an array of the flat objects; one interval keeps the flat form.</summary>
    [Fact]
    public void Range_selections_with_several_intervals_write_an_array_and_round_trip()
    {
        var low = new RangeInterval(null, 100, toInclusive: false);
        var high = RangeInterval.AtLeast(1000);
        var two = Selections.Empty.With("Amount", new RangeSelection([low, high]));
        var twoAndNull = Selections.Empty.With("Amount", new RangeSelection([low, high], includeNull: true));

        Assert.Equal("""{"Amount":{"intervals":[{"to":100,"toInclusive":false},{"from":1000}]}}""", Serializer.ToJson(two));
        Assert.Equal("""{"Amount":{"intervals":[{"to":100,"toInclusive":false},{"from":1000}],"includeNull":true}}""", Serializer.ToJson(twoAndNull));
        Assert.Equal(two, Serializer.FromJson(Serializer.ToJson(two)));
        Assert.Equal(twoAndNull, Serializer.FromJson(Serializer.ToJson(twoAndNull)));

        // Reading is lenient: an interval that cannot be read is dropped, the rest stays; an explicit unbounded interval is every value.
        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.AtLeast(1000)),
            Serializer.FromJson("""{ "Amount": { "intervals": [ { "from": 5, "to": 1 }, "text", { "from": 1000 } ] } }"""));
        Assert.Equal(Selections.Empty.With("Amount", new RangeSelection([new RangeInterval(null, null)])),
            Serializer.FromJson("""{ "Amount": { "intervals": [ {} ] } }"""));
        Assert.True(Serializer.FromJson("""{ "Amount": { "intervals": [] } }""").IsEmpty);
        Assert.True(Serializer.FromJson("""{ "Amount": { "intervals": 5 } }""").IsEmpty);
        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.OnlyNull),
            Serializer.FromJson("""{ "Amount": { "intervals": [], "includeNull": true } }"""));
    }

    [Fact]
    public void Date_selections_with_several_parts_write_an_array_and_round_trip()
    {
        var march = DateInterval.Between(TestData.Instant("2026-03-01T00:00:00+01:00"), TestData.Instant("2026-04-01T00:00:00+02:00"));
        var recent = DateInterval.Relative(DatePreset.Last7Days);
        var mixed = Selections.Empty.With("OrderDate", new DateSelection([march, recent], includeNull: true));

        Assert.Equal(
            """{"OrderDate":{"intervals":[{"from":"2026-03-01T00:00:00.0000000+01:00","to":"2026-04-01T00:00:00.0000000+02:00"},{"preset":"last7Days"}],"includeNull":true}}""",
            Serializer.ToJson(mixed));
        Assert.Equal(mixed, Serializer.FromJson(Serializer.ToJson(mixed)));
        Assert.Equal(Selections.Empty.With("OrderDate", DateSelection.Relative(DatePreset.Last7Days)),
            Serializer.FromJson("""{ "OrderDate": { "intervals": [ { "preset": "nextWeek" }, { "preset": "last7Days" } ] } }"""));
    }

    [Fact]
    public void Date_selections_write_instants_or_camel_case_presets_and_round_trip()
    {
        var from = TestData.Instant("2026-03-01T00:00:00+01:00");
        var to = TestData.Instant("2026-04-01T00:00:00+02:00");
        var absolute = Selections.Empty.With("OrderDate", DateSelection.Between(from, to));
        var openEnded = Selections.Empty.With("OrderDate", DateSelection.Between(from, null) with { IncludeNull = true });
        var relative = Selections.Empty.With("OrderDate", DateSelection.Relative(DatePreset.Last30Days));
        var onlyNull = Selections.Empty.With("OrderDate", DateSelection.OnlyNull);

        Assert.Equal("""{"OrderDate":{"from":"2026-03-01T00:00:00.0000000+01:00","to":"2026-04-01T00:00:00.0000000+02:00"}}""", Serializer.ToJson(absolute));
        Assert.Equal("""{"OrderDate":{"from":"2026-03-01T00:00:00.0000000+01:00","includeNull":true}}""", Serializer.ToJson(openEnded));
        Assert.Equal("""{"OrderDate":{"preset":"last30Days"}}""", Serializer.ToJson(relative));
        Assert.Equal("""{"OrderDate":{"onlyNull":true}}""", Serializer.ToJson(onlyNull));

        foreach (Selections selections in new[] { absolute, openEnded, relative, onlyNull })
        {
            Assert.Equal(selections, Serializer.FromJson(Serializer.ToJson(selections)));
        }

        var restored = (DateSelection)Serializer.FromJson(Serializer.ToJson(absolute))["OrderDate"]!;
        Assert.Equal(TimeSpan.FromHours(1), restored.Intervals[0].From!.Value.Offset);
    }

    [Fact]
    public void A_custom_formatter_controls_the_text_form()
    {
        var selections = Selections.Empty.With("city", ValueSelection.Of(new Address("Oslo"), null));

        string json = Serializer.ToJson(selections);
        Selections restored = Serializer.FromJson(json);

        Assert.Equal("""{"city":{"values":["Oslo",null]}}""", json);
        Assert.Equal(selections, restored);
        Assert.Equal([2, 3, 7], Shared.Calculate(restored).Items.Select(o => o.Id)); // Oslo plus the two rows without an address
    }

    [Fact]
    public void Indented_output_is_available()
    {
        string json = Serializer.ToJson(Selections.Empty.With("Country", ValueSelection.Of("SE")), indented: true);

        Assert.Contains(Environment.NewLine, json);
        Assert.Equal(Selections.Empty.With("Country", ValueSelection.Of("SE")), Serializer.FromJson(json));
    }

    [Fact]
    public void Values_arriving_as_other_json_types_are_converted_to_the_facet_type()
    {
        Selections restored = Serializer.FromJson("""
            {
              "Quantity": { "values": ["5", 2.0] },
              "Kind":     { "values": ["store", 0] },
              "Country":  { "values": [1] }
            }
            """);

        Assert.Equal(ValueSelection.Of(5, 2), restored["Quantity"]);
        Assert.Equal(ValueSelection.Of(OrderKind.Store, OrderKind.Web), restored["Kind"]);
        Assert.Equal(ValueSelection.Of("1"), restored["Country"]);
    }

    [Fact]
    public void Unknown_facets_and_unreadable_values_are_dropped_not_thrown()
    {
        Selections restored = Serializer.FromJson("""
            {
              "Nope":     { "values": ["x"] },
              "Country":  { "values": ["SE", {"nested": true}, ["array"]] },
              "Quantity": { "values": ["five", true, 7] },
              "Kind":     { "values": ["Mail"] },
              "Amount":   { "from": "abc" },
              "OrderDate": { "preset": "nextWeek" },
              "IsActive": "not an object",
              "dueDate":  { "values": [1] }
            }
            """);

        Assert.Equal(["Country", "Quantity"], restored.Keys);
        Assert.Equal(ValueSelection.Of("SE"), restored["Country"]);
        Assert.Equal(ValueSelection.Of(7), restored["Quantity"]);
    }

    [Fact]
    public void Range_and_date_shapes_that_do_not_fit_are_dropped()
    {
        Selections restored = Serializer.FromJson("""
            {
              "Amount":    { "from": 500, "to": 100 },
              "OrderDate": { "from": "2026-04-01T00:00:00Z", "to": "2026-03-01T00:00:00Z" },
              "Country":   { "from": 1, "to": 2 }
            }
            """);

        Assert.True(restored.IsEmpty);

        Assert.True(Serializer.FromJson("""{ "Amount": {} }""").IsEmpty);

        // The flat form without a bound carries no interval, so with the null flag it is the null rows alone.
        Assert.Equal(Selections.Empty.With("Amount", RangeSelection.OnlyNull),
            Serializer.FromJson("""{ "Amount": { "includeNull": true } }"""));
        Assert.True(Serializer.FromJson("""{ "Amount": { "from": 1, "toInclusive": "yes" } }""").IsEmpty);
    }

    [Fact]
    public void Non_object_roots_yield_no_selections_and_invalid_json_throws()
    {
        Assert.True(Serializer.FromJson("[]").IsEmpty);
        Assert.True(Serializer.FromJson("null").IsEmpty);
        Assert.True(Serializer.FromJson("42").IsEmpty);
        Assert.ThrowsAny<JsonException>(() => Serializer.FromJson("{ not json"));
        Assert.Throws<ArgumentNullException>(() => Serializer.FromJson(null!));
    }

    [Fact]
    public void Writing_a_selection_for_an_unknown_facet_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => Serializer.ToJson(Selections.Empty.With("Nope", ValueSelection.Of(1))));
        Assert.Throws<ArgumentException>(() => Serializer.ToJson(Selections.Empty.With("Country", RangeSelection.Between(0, 1))));
    }

    [Fact]
    public void Json_objects_can_be_used_directly()
    {
        var selections = Selections.Empty.With("Country", ValueSelection.Of("NO"));

        JsonObject json = Serializer.ToJsonObject(selections);
        json["Quantity"] = new JsonObject { ["values"] = new JsonArray(2) };

        Selections restored = Serializer.FromJsonObject(json);
        Assert.Equal(selections.With("Quantity", ValueSelection.Of(2)), restored);
    }
}
