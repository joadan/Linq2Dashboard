using System.Linq.Expressions;

namespace Linq2Dashboard.Indexing;

/// <summary>
/// Compiles a selector of any numeric property, nullable or not, into a <c>Func&lt;T, double?&gt;</c>
/// (design §3.3). The conversion is built into the expression tree so it costs one numeric cast
/// per row and no boxing. Non-numeric properties are rejected at build.
/// </summary>
internal static class NumericConversion
{
    public static Func<T, double?> ToNullableDouble<T, TProp>(Expression<Func<T, TProp>> selector, string paramName)
    {
        ArgumentNullException.ThrowIfNull(selector);
        Type underlying = Nullable.GetUnderlyingType(typeof(TProp)) ?? typeof(TProp);
        if (!IsNumeric(underlying))
        {
            throw new ArgumentException(
                $"'{typeof(TProp)}' is not a numeric type; range facets and metrics need a numeric property.", paramName);
        }

        ParameterExpression value = Expression.Variable(typeof(TProp), "value");
        Expression body;
        if (underlying == typeof(TProp))
        {
            body = Expression.Convert(Expression.Convert(value, typeof(double)), typeof(double?));
        }
        else
        {
            body = Expression.Condition(
                Expression.Property(value, "HasValue"),
                Expression.Convert(Expression.Convert(Expression.Property(value, "Value"), typeof(double)), typeof(double?)),
                Expression.Constant(null, typeof(double?)));
        }

        var block = Expression.Block(
            typeof(double?),
            [value],
            Expression.Assign(value, selector.Body),
            body);

        return Expression.Lambda<Func<T, double?>>(block, selector.Parameters).Compile();
    }

    private static bool IsNumeric(Type type) =>
        type == typeof(byte) || type == typeof(sbyte)
        || type == typeof(short) || type == typeof(ushort)
        || type == typeof(int) || type == typeof(uint)
        || type == typeof(long) || type == typeof(ulong)
        || type == typeof(float) || type == typeof(double) || type == typeof(decimal)
        || type == typeof(Half) || type == typeof(nint) || type == typeof(nuint)
        || type == typeof(Int128) || type == typeof(UInt128);
}
