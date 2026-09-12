using Microsoft.AspNetCore.Components;

namespace Linq2Dashboard.Blazor;

public partial class RangeSlider
{
    private double from;
    private double to;
    private double? lastFrom;
    private double? lastTo;
    private bool initialised;

    [Parameter, EditorRequired]
    public double Min { get; set; }

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

    [Parameter, EditorRequired]
    public IDashboardFormatter Formatter { get; set; } = default!;

    /// <summary>Number inputs beside the slider for precise entry. Default true.</summary>
    [Parameter]
    public bool ShowInputs { get; set; } = true;

    [Parameter]
    public string FromLabel { get; set; } = "From";

    [Parameter]
    public string ToLabel { get; set; } = "To";

    /// <summary>Raised on release or on a number input change with the new closed interval.</summary>
    [Parameter]
    public EventCallback<(double From, double To)> OnChange { get; set; }

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
        PreviewFrom(value) ? OnChange.InvokeAsync((from, to)) : Task.CompletedTask;

    private Task ApplyTo(object? value) =>
        PreviewTo(value) ? OnChange.InvokeAsync((from, to)) : Task.CompletedTask;

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
