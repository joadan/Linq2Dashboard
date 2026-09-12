namespace Linq2Dashboard.Blazor;

/// <summary>How <see cref="Results{T}"/> wraps its rows.</summary>
public enum ResultsLayout
{
    /// <summary>Row fragments one after another inside a <c>div</c>; the row template decides the markup.</summary>
    List,

    /// <summary>A <c>table</c>: the header template inside <c>thead</c>, rows inside <c>tbody</c>. Templates render <c>tr</c> elements.</summary>
    Table,
}
