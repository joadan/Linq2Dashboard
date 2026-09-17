using System.ComponentModel;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// One bar in <see cref="BucketBars"/>: a bucket, a period or the null value, with what a click does.
/// Like the component that renders it, a rendering detail and not part of the supported API: hidden from
/// IntelliSense and free to change without notice (design §9).
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed record BucketBar(string Label, int TotalCount, int FilteredCount, bool Selected, bool IsNull, Func<Task> Click);
