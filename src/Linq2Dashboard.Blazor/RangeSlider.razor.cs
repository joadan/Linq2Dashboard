using System.ComponentModel;
using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

/// <summary>
/// A dual-handle slider over a range facet's bounds, with optional number inputs. Raises <see cref="OnChange"/> on release with the new bounds, null for a side whose handle rests at its end (design §9).
/// A rendering detail of <see cref="RangeFacet{T}"/>, not part of the supported API: it is public only because Razor
/// components cannot be internal, is hidden from IntelliSense, and may change without notice.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public partial class RangeSlider
{
    private double from;
    private double to;
    private double? lastFrom;
    private double? lastTo;
    private bool initialised;

    /// <summary>Classes for the number inputs in place of the default <c>l2d-input</c> skin; see <see cref="RangeFacet{T}.InputClass"/>.</summary>
    [Parameter]
    public string? InputClass { get; set; }

    /// <summary>Lower end of the slider: the dataset's minimum.</summary>
    [Parameter, EditorRequired]
    public double Min { get; set; }

    /// <summary>Upper end of the slider: the dataset's maximum.</summary>
    [Parameter, EditorRequired]
    public double Max { get; set; }

    /// <summary>Current lower bound; null means <see cref="Min"/>.</summary>
    [Parameter]
    public double? From { get; set; }

    /// <summary>Current upper bound; null means <see cref="Max"/>.</summary>
    [Parameter]
    public double? To { get; set; }

    /// <summary>Handle step. Default: a power of ten near one hundredth of the range.</summary>
    [Parameter]
    public double? Step { get; set; }

    /// <summary>Formats the bound labels.</summary>
    [Parameter, EditorRequired]
    public IDashboardFormatter Formatter { get; set; } = default!;

    /// <summary>Number inputs beside the slider for precise entry. Default true.</summary>
    [Parameter]
    public bool ShowInputs { get; set; } = true;

    /// <summary>Accessible label of the lower handle and its input.</summary>
    [Parameter]
    public string FromLabel { get; set; } = "From";

    /// <summary>Accessible label of the upper handle and its input.</summary>
    [Parameter]
    public string ToLabel { get; set; } = "To";

    /// <summary>
    /// Raised on release or on a number input change with the new bounds, both inclusive. A side is null when its handle
    /// rests at the end of its travel: a handle pushed to an end means "no bound on this side", not "from the smallest
    /// value in the data", which is what lets the open-ended first and last buckets light up. The upper end is reached
    /// within one step, because a native range input snaps to a grid that starts at the minimum, so a maximum off that
    /// grid can never be hit exactly (design §9).
    /// </summary>
    [Parameter]
    public EventCallback<(double? From, double? To)> OnChange { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Max < Min)
        {
            throw new ArgumentException($"Max ({Max}) is less than Min ({Min}).");
        }

        Step ??= DefaultStep(Min, Max);
        if (Step <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Step));
        }

        // Adopt the host's bounds on first render and whenever they change; keep the handles where the user left them otherwise.
        if (!initialised || From != lastFrom || To != lastTo)
        {
            initialised = true;
            lastFrom = From;
            lastTo = To;
            from = Math.Clamp(From ?? Min, Min, Max);
            to = Math.Clamp(To ?? Max, Min, Max);
        }
    }

    private bool PreviewFrom(object? value)
    {
        if (!TryParse(value, out double v))
        {
            return false;
        }

        from = Math.Min(Math.Clamp(v, Min, Max), to);
        return true;
    }

    private bool PreviewTo(object? value)
    {
        if (!TryParse(value, out double v))
        {
            return false;
        }

        to = Math.Max(Math.Clamp(v, Min, Max), from);
        return true;
    }

    private Task ApplyFrom(object? value) =>
        PreviewFrom(value) ? Apply() : Task.CompletedTask;

    private Task ApplyTo(object? value) =>
        PreviewTo(value) ? Apply() : Task.CompletedTask;

    private Task Apply() =>
        OnChange.InvokeAsync((AtLowerEnd(from, Min) ? null : from, AtUpperEnd(to, Max, Step!.Value) ? null : to));

    /// <summary>The lower handle is at its end when it sits on the minimum, which is always on the step grid.</summary>
    internal static bool AtLowerEnd(double value, double min) => value <= min;

    /// <summary>The upper handle is at its end when it is within one step of the maximum, the closest the grid lets it get.</summary>
    internal static bool AtUpperEnd(double value, double max, double step) => max - value < step;

    private string Percent(double value) =>
        Max > Min ? ((value - Min) / (Max - Min) * 100).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%" : "0%";

    private static string Text(double value) => value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    private static string Text(double? value) => Text(value ?? 0);

    private static bool TryParse(object? value, out double result) =>
        double.TryParse(value?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result);

    /// <summary>A power of ten no larger than a hundredth of the range, at least 0.01.</summary>
    internal static double DefaultStep(double min, double max)
    {
        double range = max - min;
        if (range <= 0)
        {
            return 1;
        }

        return Math.Max(0.01, Math.Pow(10, Math.Floor(Math.Log10(range / 100))));
    }
}
