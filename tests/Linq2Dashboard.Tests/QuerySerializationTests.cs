namespace Linq2Dashboard.Tests;

/// <summary>The query-string form of selections (design §2.5): one parameter per facet, readable and hand-writable.</summary>
public class QuerySerializationTests
{
    private static readonly Dashboard<Order> Shared = Dashboard.Create(TestData.Orders(), b =>
    {
        b.ValueFacet(x => x.Country);
        b.ValueFacet(x => x.Quantity);
        b.ValueFacet(x => x.Kind);
        b.BooleanFacet(x => x.IsActive);
        b.RangeFacet(x => x.Amount);
        b.DateFacet(x => x.OrderDate).TimeZone(TestData.Stockholm);
        b.ValueFacet("city", x => x.Address).Serialize(a => a!.City, s => new Address(s));
        b.TextFacet("search", (order, text) => order.Status.Contains(text, StringComparison.OrdinalIgnoreCase));
    });

    private static SelectionSerializer Serializer => Shared.Serializer;

    private static Selections Read(string query, string prefix = "") => Serializer.FromQueryString(query, prefix);

    private static Selection? ReadOne(string key, string value) => Read($"{key}={value}")[key];

    [Fact]
    public void Empty_selections_write_an_empty_query_and_every_facet_as_null()
    {
        Assert.Equal(string.Empty, Serializer.ToQueryString(Selections.Empty));
        Assert.Equal(Selections.Empty, Read(string.Empty));

        IReadOnlyDictionary<string, string?> query = Serializer.ToQuery(Selections.Empty);
        Assert.Equal(["Country", "Quantity", "Kind", "IsActive", "Amount", "OrderDate", "city", "search"], query.Keys);
        Assert.All(query.Values, Assert.Null);
    }

    [Fact]
    public void Value_selections_write_a_comma_separated_list_with_null_as_null()
    {
        var selections = Selections.Empty
            .With("Country", ValueSelection.Of("SE", null))
            .With("Quantity", ValueSelection.Of(2, 5))
            .With("Kind", ValueSelection.Of(OrderKind.Store))
            .With("IsActive", ValueSelection.Of(true, null));

        Assert.Equal("Country=SE,null&Quantity=2,5&Kind=Store&IsActive=true,null", Serializer.ToQueryString(selections));
        Assert.Equal(selections, Read("Country=SE,null&Quantity=2,5&Kind=Store&IsActive=true,null"));
    }

    [Fact]
    public void Value_tokens_read_into_the_facets_value_type_and_unreadable_tokens_are_dropped()
    {
        Assert.Equal(ValueSelection.Of(2), ReadOne("Quantity", "2,two"));
        Assert.Equal(ValueSelection.Of(OrderKind.Web), ReadOne("Kind", "web,Shop"));
        Assert.Equal(ValueSelection.Of(false), ReadOne("IsActive", "false,maybe"));
        Assert.Null(ReadOne("Quantity", "two"));
    }

    [Fact]
    public void A_comma_a_backslash_the_text_null_and_the_empty_string_are_escaped_in_a_value_list()
    {
        var selections = Selections.Empty.With("Country", ValueSelection.Of("a,b", "back\\slash", "null", string.Empty, "\"\"", null));

        string value = Serializer.ToQuery(selections)["Country"]!;

        Assert.Equal("a\\,b,back\\\\slash,\\null,\"\",\\\"\",null", value);
        Assert.Equal(selections, Serializer.FromQuery([new KeyValuePair<string, string?>("Country", value)]));
    }

    [Fact]
    public void Empty_tokens_between_commas_are_dropped()
    {
        Assert.Equal(ValueSelection.Of("SE", "NO"), ReadOne("Country", ",SE,,NO,"));
    }

    [Fact]
    public void Range_selections_write_from_dot_dot_to_with_brackets_only_for_an_exclusive_end()
    {
        Assert.Equal("100..500", Serializer.ToQuery(Selections.Empty.With("Amount", RangeSelection.Between(100, 500)))["Amount"]);
        Assert.Equal("[100..500)", Serializer.ToQuery(Selections.Empty.With("Amount", new RangeSelection(100, 500, toInclusive: false)))["Amount"]);
        Assert.Equal("(100..500]", Serializer.ToQuery(Selections.Empty.With("Amount", new RangeSelection(100, 500, fromInclusive: false)))["Amount"]);
        Assert.Equal("100..", Serializer.ToQuery(Selections.Empty.With("Amount", RangeSelection.AtLeast(100)))["Amount"]);
        Assert.Equal("..500", Serializer.ToQuery(Selections.Empty.With("Amount", RangeSelection.AtMost(500)))["Amount"]);
        Assert.Equal("100..500,null", Serializer.ToQuery(Selections.Empty.With("Amount", new RangeSelection(100, 500, includeNull: true)))["Amount"]);
        Assert.Equal("null", Serializer.ToQuery(Selections.Empty.With("Amount", RangeSelection.OnlyNull))["Amount"]);
        Assert.Equal("0.5..1E+21", Serializer.ToQuery(Selections.Empty.With("Amount", RangeSelection.Between(0.5, 1e21)))["Amount"]);
    }

    [Fact]
    public void Range_values_read_every_written_form_and_a_single_number_as_that_value()
    {
        Assert.Equal(RangeSelection.Between(100, 500), ReadOne("Amount", "100..500"));
        Assert.Equal(new RangeSelection(100, 500, toInclusive: false), ReadOne("Amount", "[100..500)"));
        Assert.Equal(new RangeSelection(100, 500, fromInclusive: false), ReadOne("Amount", "(100..500]"));
        Assert.Equal(new RangeSelection(100, 500, fromInclusive: false, toInclusive: false), ReadOne("Amount", "(100..500)"));
        Assert.Equal(RangeSelection.AtLeast(100), ReadOne("Amount", "100.."));
        Assert.Equal(RangeSelection.AtMost(500), ReadOne("Amount", "..500"));
        Assert.Equal(new RangeSelection(100, 500, includeNull: true), ReadOne("Amount", "100..500,null"));
        Assert.Equal(RangeSelection.OnlyNull, ReadOne("Amount", "null"));
        Assert.Equal(RangeSelection.OnlyNull, ReadOne("Amount", "null,null"));
        Assert.Equal(RangeSelection.Between(100, 100), ReadOne("Amount", "100"));
        Assert.Equal(RangeSelection.Between(-5, 5), ReadOne("Amount", "-5..5"));
        // A hand-written URL with a raw plus in an exponent decodes it as a space; the number is still read.
        Assert.Equal(RangeSelection.Between(0.5, 1e21), Read("Amount=0.5..1E+21")["Amount"]);
    }

    /// <summary>Design §2.5: the intervals of one facet are a comma-separated list, like the values of a value facet, with null as one more item.</summary>
    [Fact]
    public void Range_selections_with_several_intervals_write_a_comma_separated_list_and_read_back()
    {
        var low = new RangeInterval(null, 100, toInclusive: false);
        var high = RangeInterval.AtLeast(1000);

        Assert.Equal("[..100),1000..", Serializer.ToQuery(Selections.Empty.With("Amount", new RangeSelection([low, high])))["Amount"]);
        Assert.Equal("[..100),1000..,null", Serializer.ToQuery(Selections.Empty.With("Amount", new RangeSelection([low, high], includeNull: true)))["Amount"]);

        Assert.Equal(new RangeSelection([low, high]), ReadOne("Amount", "[..100),1000.."));
        Assert.Equal(new RangeSelection([low, high], includeNull: true), ReadOne("Amount", "1000..,null,[..100)"));
        Assert.Equal(new RangeSelection([RangeInterval.Between(5, 5), RangeInterval.Between(7, 7)]), ReadOne("Amount", "5,7"));
        Assert.Equal(RangeSelection.Between(100, 500), ReadOne("Amount", "100..500,,100..500")); // duplicates and empty items collapse
    }

    [Fact]
    public void Range_values_that_do_not_parse_drop_the_selection()
    {
        Assert.Null(ReadOne("Amount", "500..100"));
        Assert.Null(ReadOne("Amount", "abc..100"));
        Assert.Null(ReadOne("Amount", ".."));
        Assert.Null(ReadOne("Amount", "..,null"));
        Assert.Null(ReadOne("Amount", "100..500,SE"));
        Assert.Null(ReadOne("Amount", "100..500,abc..1"));
        Assert.Null(ReadOne("Amount", "NaN..1"));
    }

    [Fact]
    public void Date_selections_with_several_parts_write_a_comma_separated_list_and_read_back()
    {
        var march = DateInterval.Between(TestData.Instant("2026-03-01T00:00:00+01:00"), TestData.Instant("2026-04-01T00:00:00+02:00"));
        var recent = DateInterval.Relative(DatePreset.Last7Days);
        var mixed = new DateSelection([march, recent], includeNull: true);

        Assert.Equal("2026-03-01T00:00+01:00..2026-04-01T00:00+02:00,last7Days,null", Serializer.ToQuery(Selections.Empty.With("OrderDate", mixed))["OrderDate"]);
        Assert.Equal(mixed, ReadOne("OrderDate", "last7Days,null,2026-03-01T00:00+01:00..2026-04-01T00:00+02:00"));
        Assert.Equal(new DateSelection([DateInterval.Relative(DatePreset.Today), DateInterval.Relative(DatePreset.LastYear)]), ReadOne("OrderDate", "today,LASTYEAR"));
        Assert.Null(ReadOne("OrderDate", "today,nextWeek"));
    }

    [Fact]
    public void Date_selections_write_the_preset_in_camel_case_or_a_compact_instant_interval()
    {
        Assert.Equal("last30Days", Serializer.ToQuery(Selections.Empty.With("OrderDate", DateSelection.Relative(DatePreset.Last30Days)))["OrderDate"]);
        Assert.Equal("thisYear,null", Serializer.ToQuery(Selections.Empty.With("OrderDate", DateSelection.Relative(DatePreset.ThisYear) with { IncludeNull = true }))["OrderDate"]);
        Assert.Equal("null", Serializer.ToQuery(Selections.Empty.With("OrderDate", DateSelection.OnlyNull))["OrderDate"]);

        var march = DateSelection.Between(TestData.Instant("2026-03-01T00:00:00+01:00"), TestData.Instant("2026-04-01T00:00:00+02:00"));
        Assert.Equal("2026-03-01T00:00+01:00..2026-04-01T00:00+02:00", Serializer.ToQuery(Selections.Empty.With("OrderDate", march))["OrderDate"]);

        var open = DateSelection.Between(TestData.Instant("2026-03-01T10:30:15Z"), null);
        Assert.Equal("2026-03-01T10:30:15Z..", Serializer.ToQuery(Selections.Empty.With("OrderDate", open))["OrderDate"]);

        var precise = DateSelection.Between(null, TestData.Instant("2026-03-01T10:30:00.1234500Z"));
        Assert.Equal("..2026-03-01T10:30:00.12345Z", Serializer.ToQuery(Selections.Empty.With("OrderDate", precise))["OrderDate"]);
    }

    [Fact]
    public void Date_values_read_presets_case_insensitively_instants_in_any_iso_form_and_a_plus_lost_to_a_space()
    {
        Assert.Equal(DateSelection.Relative(DatePreset.Last30Days), ReadOne("OrderDate", "LAST30DAYS"));
        Assert.Equal(DateSelection.Relative(DatePreset.Today) with { IncludeNull = true }, ReadOne("OrderDate", "today,null"));
        Assert.Equal(DateSelection.OnlyNull, ReadOne("OrderDate", "null"));

        var march = DateSelection.Between(TestData.Instant("2026-03-01T00:00:00+01:00"), TestData.Instant("2026-04-01T00:00:00+02:00"));
        Assert.Equal(march, ReadOne("OrderDate", "2026-03-01T00:00+01:00..2026-04-01T00:00+02:00"));
        Assert.Equal(march, ReadOne("OrderDate", "2026-03-01T00:00:00.0000000+01:00..2026-04-01T00:00:00.0000000+02:00"));
        // A hand-written URL with a raw plus decodes it as a space; the instant is still read.
        Assert.Equal(march, Read("OrderDate=2026-03-01T00:00+01:00..2026-04-01T00:00+02:00")["OrderDate"]);
        Assert.Equal(march, Read("OrderDate=2026-03-01T00:00%2B01:00..2026-04-01T00:00%2B02:00")["OrderDate"]);

        Assert.Null(ReadOne("OrderDate", "nextWeek"));
        Assert.Null(ReadOne("OrderDate", "2026-04-01T00:00Z..2026-03-01T00:00Z"));
        Assert.Null(ReadOne("OrderDate", "2026-03-01T00:00Z"));
        Assert.Null(ReadOne("OrderDate", ".."));
    }

    [Fact]
    public void Text_selections_are_the_text_itself_and_blank_text_drops_the_selection()
    {
        Assert.Equal("search=open%20orders", Serializer.ToQueryString(Selections.Empty.With("search", new TextSelection("open orders"))));
        Assert.Equal(new TextSelection("open orders"), Read("search=open+orders")["search"]);
        Assert.Equal(new TextSelection("open orders"), Read("search=open%20orders")["search"]);
        Assert.Null(ReadOne("search", "%20%20"));
    }

    [Fact]
    public void An_application_supplied_format_writes_and_reads_the_value()
    {
        var selections = Selections.Empty.With("city", ValueSelection.Of(new Address("Oslo"), null));

        Assert.Equal("city=Oslo,null", Serializer.ToQueryString(selections));
        Assert.Equal(selections, Read("city=Oslo,null"));
    }

    [Fact]
    public void Only_the_unsafe_characters_are_percent_encoded_so_the_url_stays_readable()
    {
        var selections = Selections.Empty
            .With("Country", ValueSelection.Of("a b", "x&y=z", "50%", "ö", "#1", "c/d:e"))
            .With("Amount", new RangeSelection(100, 500, toInclusive: false));

        string query = Serializer.ToQueryString(selections);

        Assert.Equal("Country=a%20b,x%26y%3Dz,50%25,%C3%B6,%231,c/d:e&Amount=[100..500)", query);
        Assert.Equal(selections, Read(query));
    }

    [Fact]
    public void A_prefix_keeps_two_dashboards_on_one_page_apart()
    {
        var orders = Selections.Empty.With("Country", ValueSelection.Of("SE"));
        var returns = Selections.Empty.With("Country", ValueSelection.Of("NO"));

        Assert.Equal("o.Country=SE", Serializer.ToQueryString(orders, "o."));
        Assert.Equal(["o.Country", "o.Quantity", "o.Kind", "o.IsActive", "o.Amount", "o.OrderDate", "o.city", "o.search"], Serializer.ToQuery(orders, "o.").Keys);

        const string url = "?o.Country=SE&r.Country=NO&tab=details";
        Assert.Equal(orders, Read(url, "o."));
        Assert.Equal(returns, Read(url, "r."));
        Assert.Equal(Selections.Empty, Read(url));
    }

    [Fact]
    public void Writing_into_an_existing_query_keeps_the_pages_parameters_and_replaces_the_facets()
    {
        var selections = Selections.Empty.With("Amount", RangeSelection.Between(100, 500));

        string query = Serializer.ToQueryString(selections, existingQuery: "https://host/page?rows=100&Country=DK&o.Country=SE&Amount=1..2#top");

        Assert.Equal("rows=100&o.Country=SE&Amount=100..500", query);
        Assert.Equal("rows=100&o.Country=SE", Serializer.ToQueryString(Selections.Empty, existingQuery: query));
        Assert.Equal("o.Amount=100..500", Serializer.ToQueryString(selections, "o.", existingQuery: "?o.Country=SE"));
    }

    [Fact]
    public void Reading_accepts_a_whole_url_a_leading_question_mark_and_ignores_the_fragment()
    {
        var expected = Selections.Empty.With("Country", ValueSelection.Of("SE"));

        Assert.Equal(expected, Read("https://host/page?rows=100&Country=SE#Amount=1..2"));
        Assert.Equal(expected, Read("?Country=SE"));
        Assert.Equal(expected, Read("Country=SE&&unknown=1&Amount"));
    }

    [Fact]
    public void The_last_of_several_parameters_with_the_same_name_wins()
    {
        Assert.Equal(ValueSelection.Of("NO"), Read("Country=SE&Country=NO")["Country"]);
        Assert.Null(Read("Country=SE&Country=")["Country"]);
    }

    [Fact]
    public void Writing_a_selection_for_an_unknown_facet_throws()
    {
        var selections = Selections.Empty.With("nope", ValueSelection.Of(1));

        Assert.Throws<ArgumentException>(() => Serializer.ToQuery(selections));
        Assert.Throws<ArgumentException>(() => Serializer.ToQueryString(selections));
    }

    [Fact]
    public void Every_kind_round_trips_through_the_query_string()
    {
        var selections = Selections.Empty
            .With("Country", ValueSelection.Of("SE", "NO", null))
            .With("Quantity", ValueSelection.Of(2))
            .With("IsActive", ValueSelection.Of(true))
            .With("Amount", new RangeSelection(100, 500, toInclusive: false, includeNull: true))
            .With("OrderDate", DateSelection.Between(TestData.Instant("2026-03-01T00:00:00+01:00"), TestData.Instant("2026-04-01T00:00:00+02:00")))
            .With("city", ValueSelection.Of(new Address("Oslo")))
            .With("search", new TextSelection("acme"));

        Assert.Equal(selections, Read(Serializer.ToQueryString(selections)));
        Assert.Equal(selections, Serializer.FromQuery(Serializer.ToQuery(selections)));
    }
}
