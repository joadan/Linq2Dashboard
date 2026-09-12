namespace Linq2Dashboard.Indexing;

/// <summary>
/// Reads the value of one row for a column being built. Returns <c>false</c> when the row has no
/// value (null), in which case <paramref name="value"/> is ignored. This lets columns stay generic
/// over the non-nullable value type while the facet definition layer, which knows the real
/// property type, handles reference nulls and <see cref="Nullable{T}"/>.
/// </summary>
internal delegate bool RowReader<TValue>(int row, out TValue value) where TValue : notnull;
