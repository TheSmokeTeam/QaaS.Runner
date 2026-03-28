namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Legacy date and time conversion helpers that preserve the runner's historical daylight-saving behavior.
/// </summary>
public static class DateTimeExtensions
{
    /// <summary>
     /// Converts a local wall-clock value into UTC using a summer-time offset and optional DST override.
     /// </summary>
    /// <param name="timeToConvertToUtc"> the datetime to convert to utc </param>
    /// <param name="insertionTimeTimeZoneOffsetSummerTime">
    ///     The timezone offset during summer time
    ///     (for example if local summer time is gmt + 3 this value is 3)
    /// </param>
    /// <param name="isDayLightSavingTime">
    ///     True if its day light saving time right now in the configured time zone,
    ///     if no value is given gets the time zone info from the system
    /// </param>
    /// <param name="timeZoneId">
    ///     The time zone id used when daylight-saving resolution is needed.
    ///     Defaults to <see cref="TimeZoneInfoResolver.DefaultTimeZoneId" />.
    /// </param>
    /// <returns>The converted UTC value with <see cref="DateTimeKind.Utc" />.</returns>
    public static DateTime ConvertDateTimeToUtcByTimeZoneOffset(
        this DateTime timeToConvertToUtc,
        int insertionTimeTimeZoneOffsetSummerTime,
        bool? isDayLightSavingTime = null,
        string? timeZoneId = null)
    {
        isDayLightSavingTime ??= IsDayLightSavingTimeInGivenDateTime(timeToConvertToUtc, timeZoneId);
        var dateTimeConvertedToUtc =
            timeToConvertToUtc - TimeSpan.FromHours(insertionTimeTimeZoneOffsetSummerTime);

        if (insertionTimeTimeZoneOffsetSummerTime != 0 && !isDayLightSavingTime.Value)
            dateTimeConvertedToUtc += TimeSpan.FromHours(1);

        return DateTime.SpecifyKind(dateTimeConvertedToUtc, DateTimeKind.Utc);
    }

    /// <summary>
    /// Converts a UTC value into a local wall-clock value using a summer-time offset and optional DST override.
    /// </summary>
    /// <param name="utcTimeToConvert"> the utc datetime to convert </param>
    /// <param name="timeZoneOffsetSummerTime">
    ///     The timezone offset during summer time
    ///     (for example if local summer time is gmt + 3 this value is 3)
    /// </param>
    /// <param name="isDayLightSavingTime">
    ///     True if its day light saving time right now in the configured time zone,
    ///     if no value is given gets the time zone info from the system
    /// </param>
    /// <param name="timeZoneId">
    ///     The time zone id used when daylight-saving resolution is needed.
    ///     Defaults to <see cref="TimeZoneInfoResolver.DefaultTimeZoneId" />.
    /// </param>
    /// <returns>The converted local value with <see cref="DateTimeKind.Unspecified" />.</returns>
    public static DateTime ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset(
        this DateTime utcTimeToConvert,
        int timeZoneOffsetSummerTime,
        bool? isDayLightSavingTime = null,
        string? timeZoneId = null)
    {
        isDayLightSavingTime ??= IsDayLightSavingTimeInGivenDateTime(utcTimeToConvert, timeZoneId);
        var dateTimeWithTimeZoneOffset =
            utcTimeToConvert + TimeSpan.FromHours(timeZoneOffsetSummerTime);

        if (timeZoneOffsetSummerTime != 0 && !isDayLightSavingTime.Value)
            dateTimeWithTimeZoneOffset -= TimeSpan.FromHours(1);

        return DateTime.SpecifyKind(dateTimeWithTimeZoneOffset, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// Determines whether the supplied date falls inside daylight-saving time in the configured timezone.
    /// </summary>
    private static bool IsDayLightSavingTimeInGivenDateTime(this DateTime dateTime, string? timeZoneId = null)
    {
        return TimeZoneInfoResolver.ResolveTimeZoneInfo(timeZoneId).IsDaylightSavingTime(dateTime);
    }
}
