namespace Linq2Dashboard.Indexing;

/// <summary>
/// Reads the value of one row for a column being built. Returns <c>false</c> when the row has no
/// value (null), in which case <paramref name="value"/> is ignored. Columns never see a null value
/// through this protocol, which is what lets them use <typeparamref name="TValue"/> as a dictionary
/// key even when it is a reference type or a <see cref="Nullable{T}"/>.
/// </summary>
internal delegate bool RowReader<TValue>(int row, out TValue value);
