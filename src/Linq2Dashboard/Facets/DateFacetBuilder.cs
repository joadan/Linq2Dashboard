namespace Linq2Dashboard;

/// <summary>Fluent configuration of a date facet (design §2.1).</summary>
public sealed class DateFacetBuilder<T>
{
    private readonly Action<string> _setTitle;
    private readonly Action<TimeZoneInfo> _setZone;
    private readonly Action<DateGranularity> _setGranularity;
    private readonly Action<DatePreset[]> _setPresets;
    private readonly Action _ensureMutable;

    internal DateFacetBuilder(
        string key,
        Action<string> setTitle,
        Action<TimeZoneInfo> setZone,
        Action<DateGranularity> setGranularity,
        Action<DatePreset[]> setPresets,
        Action ensureMutable)
    {
        Key = key;
        _setTitle = setTitle;
        _setZone = setZone;
        _setGranularity = setGranularity;
        _setPresets = setPresets;
        _ensureMutable = ensureMutable;
    }

    public string Key { get; }

    /// <summary>Display name. Defaults to the key.</summary>
    public DateFacetBuilder<T> Title(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _ensureMutable();
        _setTitle(title);
        return this;
    }

    /// <summary>Time zone for calendar buckets and presets. Default is UTC (concept §5).</summary>
    public DateFacetBuilder<T> TimeZone(TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        _ensureMutable();
        _setZone(zone);
        return this;
    }

    /// <summary>Calendar period for buckets. Default is month.</summary>
    public DateFacetBuilder<T> Granularity(DateGranularity granularity)
    {
        if (!Enum.IsDefined(granularity))
        {
            throw new ArgumentOutOfRangeException(nameof(granularity));
        }

        _ensureMutable();
        _setGranularity(granularity);
        return this;
    }

    /// <summary>Relative presets to offer and count (concept §5). Default is none.</summary>
    public DateFacetBuilder<T> Presets(params DatePreset[] presets)
    {
        ArgumentNullException.ThrowIfNull(presets);
        foreach (DatePreset preset in presets)
        {
            if (!Enum.IsDefined(preset))
            {
                throw new ArgumentOutOfRangeException(nameof(presets), $"Unknown preset {preset}.");
            }
        }

        _ensureMutable();
        _setPresets(presets.Distinct().ToArray());
        return this;
    }
}
