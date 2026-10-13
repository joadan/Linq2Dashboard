namespace Linq2Dashboard;

/// <summary>
/// A query that cannot be answered as asked (design §10.6): an unknown name, a part that does not fit what it
/// names, a value JSON cannot read. Thrown before any row is read. The message says what is wrong and what is
/// known, the nearest match included, so it can be passed back to a person or an agent as it is.
/// </summary>
public sealed class DashboardQueryException : ArgumentException
{
    internal DashboardQueryException(string message, string? path = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Path = path;
    }

    /// <summary>
    /// The part of the query at fault, in the JSON shape's terms: <c>groupBy[1]</c>, <c>values[0]</c>,
    /// <c>columns[2]</c>, <c>orderBy</c>, <c>take</c>, <c>where</c>, <c>selections.Country</c>. Null when the
    /// query as a whole is at fault.
    /// </summary>
    public string? Path { get; }

    /// <summary>The same error at <paramref name="path"/>, unless it already names a more precise one.</summary>
    internal DashboardQueryException At(string path) =>
        Path is null ? new DashboardQueryException(Message, path, InnerException) : this;
}
