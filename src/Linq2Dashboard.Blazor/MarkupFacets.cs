using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// Turns a component's boxed <c>For</c> selector and definition parameters into typed builder calls (design §9). The
/// components know only the row type, so the value type is read from the selector and the typed builder method is
/// reached through one generic method per facet kind.
/// </summary>
internal static class MarkupFacets
{
    /// <summary>
    /// <paramref name="selector"/> without the conversion to <c>object</c> that a value-type member gets in an
    /// <c>Expression&lt;Func&lt;T, object?&gt;&gt;</c>, so its return type is the member's own.
    /// </summary>
    public static LambdaExpression Unbox<T>(Expression<Func<T, object?>> selector)
    {
        Expression body = selector.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert
            ? convert.Operand
            : selector.Body;
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(typeof(T), body.Type), body, selector.Parameters);
    }

    /// <summary>The item type of a collection type, or null for a type that is not one; a string is not a collection here.</summary>
    public static Type? ItemType(Type type)
    {
        if (type == typeof(string))
        {
            return null;
        }

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            return type.GetGenericArguments()[0];
        }

        return type.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            ?.GetGenericArguments()[0];
    }

    /// <summary>Defines a value, boolean or multi-valued facet from a <see cref="ValueFacet{T}"/>'s parameters.</summary>
    public static void Value<T>(DashboardBuilder<T> builder, string key, LambdaExpression selector, ValueFacetParameters<T> options)
    {
        Type type = selector.ReturnType;
        Type? item = ItemType(type);
        if (options.Multiple)
        {
            if (item is null)
            {
                throw new InvalidOperationException(
                    $"ValueFacet '{key}' has Multiple, but its selector returns {type.Name}, not a collection. Leave Multiple off for a single-valued facet.");
            }

            if (options.Label is not null)
            {
                throw new InvalidOperationException(
                    $"ValueFacet '{key}' is multi-valued, so a label reads the value, not the row: set it in Define, (MultiValueFacetBuilder<{typeof(T).Name}, {item.Name}> f) => f.Label(...).");
            }

            Invoke(nameof(MultiValue), [typeof(T), item], builder, key, selector, options);
            return;
        }

        if (item is not null)
        {
            throw new InvalidOperationException(
                $"ValueFacet '{key}' has a selector that returns a collection of {item.Name}. Set Multiple=\"true\" for a multi-valued facet, which counts a row under each of its values.");
        }

        Invoke(nameof(SingleValue), [typeof(T), type], builder, key, selector, options);
    }

    private static void SingleValue<T, TProp>(DashboardBuilder<T> builder, string key, LambdaExpression selector, ValueFacetParameters<T> options)
    {
        var typed = (Expression<Func<T, TProp>>)selector;
        ValueFacetBuilder<T, TProp> facet = typed switch
        {
            Expression<Func<T, bool>> flag => (ValueFacetBuilder<T, TProp>)(object)builder.BooleanFacet(key, flag),
            Expression<Func<T, bool?>> flag => (ValueFacetBuilder<T, TProp>)(object)builder.BooleanFacet(key, flag),
            _ => builder.ValueFacet(key, typed),
        };

        if (options.Name is not null)
        {
            facet.Name(options.Name);
        }

        if (options.Top is int top)
        {
            facet.Top(top);
        }

        if (options.RankBy is RankMode rank)
        {
            facet.RankBy(rank);
        }

        if (options.Searchable is bool searchable)
        {
            facet.Searchable(searchable);
        }

        if (options.Label is not null)
        {
            facet.Label(options.Label);
        }

        ApplyDefine(options.Define, facet, key);
    }

    private static void MultiValue<T, TItem>(DashboardBuilder<T> builder, string key, LambdaExpression selector, ValueFacetParameters<T> options)
    {
        var typed = Expression.Lambda<Func<T, IEnumerable<TItem>?>>(Expression.Convert(selector.Body, typeof(IEnumerable<TItem>)), selector.Parameters);
        MultiValueFacetBuilder<T, TItem> facet = builder.MultiValueFacet(key, typed);
        if (options.Name is not null)
        {
            facet.Name(options.Name);
        }

        if (options.Top is int top)
        {
            facet.Top(top);
        }

        if (options.RankBy is RankMode rank)
        {
            facet.RankBy(rank);
        }

        if (options.Searchable is bool searchable)
        {
            facet.Searchable(searchable);
        }

        ApplyDefine(options.Define, facet, key);
    }

    /// <summary>Defines a range facet from a <see cref="RangeFacet{T}"/>'s parameters; the core checks that the selector is numeric.</summary>
    public static void Range<T>(DashboardBuilder<T> builder, string key, LambdaExpression selector, RangeFacetParameters<T> options)
    {
        if (options.Buckets is not null && options.AutoBuckets is not null)
        {
            throw new InvalidOperationException($"RangeFacet '{key}' takes Buckets or AutoBuckets, not both.");
        }

        Invoke(nameof(RangeOf), [typeof(T), selector.ReturnType], builder, key, selector, options);
    }

    private static void RangeOf<T, TProp>(DashboardBuilder<T> builder, string key, LambdaExpression selector, RangeFacetParameters<T> options)
    {
        RangeFacetBuilder<T> facet = builder.RangeFacet(key, (Expression<Func<T, TProp>>)selector);
        if (options.Name is not null)
        {
            facet.Name(options.Name);
        }

        if (options.Buckets is not null)
        {
            facet.Buckets(options.Buckets);
        }

        if (options.AutoBuckets is int count)
        {
            facet.AutoBuckets(count);
        }

        options.Define?.Invoke(facet);
    }

    /// <summary>Defines a date facet from a <see cref="DateFacet{T}"/>'s parameters; the core checks that the selector is a date type.</summary>
    public static void Date<T>(DashboardBuilder<T> builder, string key, LambdaExpression selector, DateFacetParameters<T> options)
    {
        if (options.Granularity is not null && options.AutoGranularity is not null)
        {
            throw new InvalidOperationException($"DateFacet '{key}' takes Granularity or AutoGranularity, not both.");
        }

        Invoke(nameof(DateOf), [typeof(T), selector.ReturnType], builder, key, selector, options);
    }

    private static void DateOf<T, TDate>(DashboardBuilder<T> builder, string key, LambdaExpression selector, DateFacetParameters<T> options)
    {
        DateFacetBuilder<T> facet = builder.DateFacet(key, (Expression<Func<T, TDate>>)selector);
        if (options.Name is not null)
        {
            facet.Name(options.Name);
        }

        if (options.TimeZone is not null)
        {
            facet.TimeZone(options.TimeZone);
        }

        if (options.Granularity is DateGranularity granularity)
        {
            facet.Granularity(granularity);
        }

        if (options.AutoGranularity is int maxPeriods)
        {
            facet.AutoGranularity(maxPeriods);
        }

        if (options.Presets is not null)
        {
            facet.Presets(options.Presets);
        }

        if (options.SkipEmptyPresets is bool skip)
        {
            facet.SkipEmptyPresets(skip);
        }

        options.Define?.Invoke(facet);
    }

    /// <summary>Defines a text facet from a <see cref="TextFacet{T}"/>'s parameters.</summary>
    public static void Text<T>(DashboardBuilder<T> builder, string key, Func<T, string, bool> match, string? name, Action<TextFacetBuilder<T>>? define)
    {
        TextFacetBuilder<T> facet = builder.TextFacet(key, match);
        if (name is not null)
        {
            facet.Name(name);
        }

        define?.Invoke(facet);
    }

    /// <summary>
    /// Runs a <c>Define</c> delegate on the typed facet builder. The component cannot type the parameter, since the value
    /// type comes from the selector, so the delegate names the builder type itself and a mismatch fails when the facet is defined.
    /// </summary>
    private static void ApplyDefine<TBuilder>(Delegate? define, TBuilder facet, string key)
    {
        if (define is null)
        {
            return;
        }

        if (define is not Action<TBuilder> typed)
        {
            string takes = string.Join(", ", define.Method.GetParameters().Select(p => Readable(p.ParameterType)));
            throw new InvalidOperationException(
                $"Define on ValueFacet '{key}' must take the facet's builder, {Readable(typeof(TBuilder))}, and return nothing; it takes ({takes}). " +
                $"Write the parameter type in the lambda: Define=\"({Readable(typeof(TBuilder))} f) => ...\".");
        }

        typed(facet);
    }

    /// <summary>A type name as C# writes it, generic arguments included, for messages.</summary>
    private static string Readable(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        if (Nullable.GetUnderlyingType(type) is { } inner)
        {
            return Readable(inner) + "?";
        }

        string name = type.Name[..type.Name.IndexOf('`')];
        return $"{name}<{string.Join(", ", type.GetGenericArguments().Select(Readable))}>";
    }

    private static void Invoke(string method, Type[] typeArguments, params object?[] arguments)
    {
        MethodInfo generic = typeof(MarkupFacets).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(typeArguments);
        try
        {
            generic.Invoke(null, arguments);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }
    }
}

/// <summary>The definition parameters of a <see cref="ValueFacet{T}"/>, read when the facet is defined (design §9).</summary>
internal sealed record ValueFacetParameters<T>(string? Name, int? Top, RankMode? RankBy, bool? Searchable, Func<T, string?>? Label, bool Multiple, Delegate? Define);

/// <summary>The definition parameters of a <see cref="RangeFacet{T}"/>, read when the facet is defined (design §9).</summary>
internal sealed record RangeFacetParameters<T>(string? Name, double[]? Buckets, int? AutoBuckets, Action<RangeFacetBuilder<T>>? Define);

/// <summary>The definition parameters of a <see cref="DateFacet{T}"/>, read when the facet is defined (design §9).</summary>
internal sealed record DateFacetParameters<T>(
    string? Name,
    TimeZoneInfo? TimeZone,
    DateGranularity? Granularity,
    int? AutoGranularity,
    DatePreset[]? Presets,
    bool? SkipEmptyPresets,
    Action<DateFacetBuilder<T>>? Define);
