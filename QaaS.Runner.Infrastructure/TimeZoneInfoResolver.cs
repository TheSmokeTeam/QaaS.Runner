namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Resolves time zone identifiers across operating systems while keeping a single default zone.
/// </summary>
public static class TimeZoneInfoResolver
{
    public const string DefaultTimeZoneId = "Asia/Jerusalem";

    public static TimeZoneInfo ResolveTimeZoneInfo(string? timeZoneId = null)
    {
        var effectiveTimeZoneId = string.IsNullOrWhiteSpace(timeZoneId)
            ? DefaultTimeZoneId
            : timeZoneId;

        if (TryResolve(effectiveTimeZoneId, out var timeZoneInfo))
            return timeZoneInfo;

        throw new TimeZoneNotFoundException($"Could not resolve time zone '{effectiveTimeZoneId}'.");
    }

    private static bool TryResolve(string timeZoneId, out TimeZoneInfo timeZoneInfo)
    {
        if (TryFindSystemTimeZone(timeZoneId, out timeZoneInfo))
            return true;

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsTimeZoneId) &&
            TryFindSystemTimeZone(windowsTimeZoneId, out timeZoneInfo))
            return true;

        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaTimeZoneId) &&
            TryFindSystemTimeZone(ianaTimeZoneId, out timeZoneInfo))
            return true;

        timeZoneInfo = null!;
        return false;
    }

    private static bool TryFindSystemTimeZone(string timeZoneId, out TimeZoneInfo timeZoneInfo)
    {
        try
        {
            timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timeZoneInfo = null!;
            return false;
        }
    }
}
