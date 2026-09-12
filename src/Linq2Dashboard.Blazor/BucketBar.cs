namespace Linq2Dashboard.Blazor;

/// <summary>One bar in <see cref="BucketBars"/>: a bucket, a period or the null value, with what a click does.</summary>
public sealed record BucketBar(string Label, int TotalCount, int FilteredCount, bool Selected, bool IsNull, Func<Task> Click);
