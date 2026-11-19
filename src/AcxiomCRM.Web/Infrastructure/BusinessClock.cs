using Microsoft.Extensions.Options;

namespace AcxiomCRM.Web.Infrastructure;

/// <summary>
/// Single source of "now" and "today". Audit/creation timestamps are stored in UTC; schedule-type
/// dates (follow-up date, expected close date) are business-local in the configured time zone.
/// </summary>
public interface IBusinessClock
{
    DateTime UtcNow { get; }
    DateTime Now { get; }
    DateTime Today { get; }
    DateTime ToLocal(DateTime utc);
    DateTime ToUtc(DateTime local);
}

public sealed class BusinessClock : IBusinessClock
{
    private readonly TimeProvider _time;
    private readonly TimeZoneInfo _zone;

    public BusinessClock(TimeProvider time, IOptions<AppOptions> options)
    {
        _time = time;
        _zone = ResolveZone(options.Value.TimeZoneId);
    }

    public DateTime UtcNow => _time.GetUtcNow().UtcDateTime;
    public DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(UtcNow, _zone);
    public DateTime Today => Now.Date;

    public DateTime ToLocal(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone);

    public DateTime ToUtc(DateTime local) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _zone);

    private static TimeZoneInfo ResolveZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }
}
