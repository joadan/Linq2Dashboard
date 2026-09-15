using System.Globalization;

namespace Linq2Dashboard.Tests;

public enum OrderKind
{
    Web,
    Store,
}

public sealed record Address(string City);

public sealed record Order(
    int Id,
    string? Country,
    string Status,
    bool IsActive,
    bool? Verified,
    decimal Amount,
    decimal? Discount,
    int? Quantity,
    DateTime OrderDate,
    DateTimeOffset? Shipped,
    DateOnly Due,
    OrderKind Kind,
    Address? Address);

public static class TestData
{
    public static readonly TimeZoneInfo Stockholm = TimeZoneInfo.FindSystemTimeZoneById("Europe/Stockholm");

    public static DateTimeOffset Instant(string iso) => DateTimeOffset.Parse(iso, CultureInfo.InvariantCulture);

    public static DateTime Local(string iso) => DateTime.Parse(iso, CultureInfo.InvariantCulture);

    /// <summary>Eight orders with nulls sprinkled over every nullable property.</summary>
    public static Order[] Orders() =>
    [
        //        Id  Country Status     Active Verified Amount Discount Quantity OrderDate                       Shipped                             Due                     Kind             Address
        new Order(1,  "SE",   "Open",    true,  true,    100m,  10m,     2,       Local("2026-01-15T10:00:00"),   Instant("2026-01-16T08:00:00Z"),   new DateOnly(2026, 1, 20), OrderKind.Web,   new Address("Stockholm")),
        new Order(2,  "NO",   "Open",    true,  null,    250m,  null,    null,    Local("2026-02-03T09:30:00"),   null,                              new DateOnly(2026, 2, 10), OrderKind.Store, new Address("Oslo")),
        new Order(3,  null,   "Closed",  false, false,   500m,  50m,     1,       Local("2026-02-20T16:00:00"),   Instant("2026-02-21T08:00:00Z"),   new DateOnly(2026, 2, 28), OrderKind.Web,   null),
        new Order(4,  "SE",   "Closed",  true,  true,    999.5m, 0m,     5,       Local("2026-03-01T23:30:00"),   Instant("2026-03-02T08:00:00Z"),   new DateOnly(2026, 3, 5),  OrderKind.Web,   new Address("Göteborg")),
        new Order(5,  "DK",   "Pending", false, null,    1000m, null,    3,       Local("2026-03-10T12:00:00"),   null,                              new DateOnly(2026, 3, 15), OrderKind.Store, new Address("Copenhagen")),
        new Order(6,  "se",   "Open",    true,  false,   2500m, 250m,    null,    Local("2026-03-31T22:30:00"),   Instant("2026-04-01T08:00:00Z"),   new DateOnly(2026, 4, 5),  OrderKind.Web,   new Address("Malmö")),
        new Order(7,  null,   "Pending", false, true,    0m,    null,    0,       Local("2026-04-02T08:00:00"),   null,                              new DateOnly(2026, 4, 9),  OrderKind.Store, null),
        new Order(8,  "NO",   "Open",    true,  true,    75m,   5m,      2,       Local("2026-04-15T14:00:00"),   Instant("2026-04-16T08:00:00Z"),   new DateOnly(2026, 4, 20), OrderKind.Web,   new Address("Bergen")),
    ];
}

/// <summary>A clock that stands still.</summary>
public sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>Counts how many times it is enumerated.</summary>
public sealed class CountingEnumerable<T>(IEnumerable<T> inner) : IEnumerable<T>
{
    public int Enumerations { get; private set; }

    public IEnumerator<T> GetEnumerator()
    {
        Enumerations++;
        return inner.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A clock the test moves by hand.</summary>
public sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
