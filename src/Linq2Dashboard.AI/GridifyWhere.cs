using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Gridify;

namespace Linq2Dashboard.AI;

/// <summary>One field the where can name, with its kind in the vocabulary of the description (design §10.7).</summary>
internal sealed record WhereField(string Name, PropertyKind Kind);

/// <summary>
/// The where in Gridify syntax over plain properties of <typeparamref name="T"/> (design §10.10), with the value facets'
/// semantics: strings compare ignoring case, and a null string is the empty one, so <c>Country=</c> and
/// <c>Country=null</c> find it and no filter throws on it, the trap Gridify has with case-insensitive filtering (design §10.9).
/// </summary>
internal sealed class GridifyWhere<T>
{
    private readonly GridifyMapper<T> mapper;

    private GridifyWhere(GridifyMapper<T> mapper, IReadOnlyList<WhereField> fields)
    {
        this.mapper = mapper;
        Fields = fields;
    }

    public IReadOnlyList<WhereField> Fields { get; }

    public string Help =>
        $"Gridify syntax, such as {Example}. Fields: {string.Join(", ", Fields.Select(f => $"{f.Name} ({Kind(f.Kind)})"))}.";

    private string Example => Fields.FirstOrDefault(f => f.Kind == PropertyKind.Number) is WhereField number
        ? $"{number.Name}>=100"
        : $"{Fields[0].Name}=x";

    public Expression<Func<T, bool>> Parse(string text) =>
        new GridifyQuery { Filter = text }.GetFilteringExpression(mapper);

    /// <summary>
    /// The where over <paramref name="filterable"/>, or over every allowed property a filter can compare when it is null.
    /// Null when no property is left. A name given in <paramref name="filterable"/> that is not such a property throws.
    /// </summary>
    public static GridifyWhere<T>? Create(IReadOnlyCollection<string>? filterable, IReadOnlyCollection<string>? allowed)
    {
        Dictionary<string, PropertyInfo> properties = typeof(T)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToDictionary(p => p.Name, StringComparer.Ordinal);

        List<PropertyInfo> chosen = [];
        if (filterable is not null)
        {
            foreach (string name in filterable)
            {
                if (!properties.TryGetValue(name, out PropertyInfo? property))
                {
                    throw new ArgumentException($"Filterable names '{name}', which is not a public property of {typeof(T).Name}.", nameof(filterable));
                }

                if (KindOf(property.PropertyType) is null)
                {
                    throw new ArgumentException($"Filterable names '{name}', a {property.PropertyType.Name} a filter cannot compare: use text, numbers, booleans, enums, Guid or dates.", nameof(filterable));
                }

                if (allowed is not null && !allowed.Contains(name))
                {
                    throw new ArgumentException($"Filterable names '{name}', which Allowed leaves out.", nameof(filterable));
                }

                chosen.Add(property);
            }
        }
        else
        {
            chosen.AddRange(properties.Values.Where(p => KindOf(p.PropertyType) is not null && (allowed is null || allowed.Contains(p.Name))));
        }

        if (chosen.Count == 0)
        {
            return null;
        }

        var mapper = new GridifyMapper<T>(c => c.CaseInsensitiveFiltering = true);
        List<WhereField> fields = [];
        foreach (PropertyInfo property in chosen.OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            Map(mapper, property);
            fields.Add(new WhereField(property.Name, KindOf(property.PropertyType)!.Value));
        }

        return new GridifyWhere<T>(mapper, fields);
    }

    private static void Map(GridifyMapper<T> mapper, PropertyInfo property)
    {
        ParameterExpression row = Expression.Parameter(typeof(T), "x");
        Expression value = Expression.Property(row, property);
        Func<string, object>? convert = null;
        if (property.PropertyType == typeof(string))
        {
            value = Expression.Coalesce(value, Expression.Constant(string.Empty));
            convert = text => text.Equals("null", StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
        }
        else if (KindOf(property.PropertyType) == PropertyKind.Number)
        {
            // Gridify reads numbers in the current culture, so 99.5 fails under a decimal comma; an agent writes them invariant.
            Type plain = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            convert = text => decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal number)
                ? Convert.ChangeType(number, plain, CultureInfo.InvariantCulture)
                : text;
        }

        mapper.AddMap(property.Name, Expression.Lambda<Func<T, object?>>(Expression.Convert(value, typeof(object)), row), convert);
    }

    private static PropertyKind? KindOf(Type type)
    {
        Type plain = Nullable.GetUnderlyingType(type) ?? type;
        if (plain == typeof(string) || plain == typeof(Guid) || plain.IsEnum)
        {
            return PropertyKind.Text;
        }

        if (plain == typeof(bool))
        {
            return PropertyKind.Boolean;
        }

        if (plain == typeof(DateTime) || plain == typeof(DateTimeOffset) || plain == typeof(DateOnly))
        {
            return PropertyKind.Date;
        }

        return Type.GetTypeCode(plain) switch
        {
            TypeCode.Byte or TypeCode.SByte or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
                or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double or TypeCode.Decimal => PropertyKind.Number,
            _ => null,
        };
    }

    public static string Kind(PropertyKind kind) => kind switch
    {
        PropertyKind.Number => "number",
        PropertyKind.Boolean => "boolean",
        PropertyKind.Date => "date",
        _ => "text",
    };
}
