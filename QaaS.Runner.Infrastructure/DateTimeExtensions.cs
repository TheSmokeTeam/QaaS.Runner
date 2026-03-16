namespace QaaS.Runner.Infrastructure;

/// <summary>
/// Legacy date and time conversion helpers that apply the runner's historical Israel timezone rules.
/// </summary>
public static class DateTimeExtensions
{
    private static readonly string IsraelTimeZoneId = Environment.OSVersion.Platform == PlatformID.Unix
        ? Path.Join("Asia", "Jerusalem")
        : "Israel Standard Time";

    /// <summary>
    /// Converts a local wall-clock value into UTC using a summer-time offset and optional DST override.
    /// </summary>
    /// <param name="timeToConvertToUtc"> the datetime to convert to utc </param>
    /// <param name="insertionTimeTimeZoneOffsetSummerTime">
    ///     The timezone offset during summer time
    ///     (for example in israel its 3 for gmt + 3 during summer)
    /// </param>
    /// <param name="isDayLightSavingTime">
    ///     True if its day light saving time right now in israel time zone,
    ///     if no value is given gets the time zone info from the system
    /// </param>
    /// <returns>The converted UTC value with <see cref="DateTimeKind.Utc" />.</returns>
    public static DateTime ConvertDateTimeToUtcByTimeZoneOffset(
        this DateTime timeToConvertToUtc, int insertionTimeTimeZoneOffsetSummerTime, bool? isDayLightSavingTime = null)
    {
        isDayLightSavingTime ??= IsDayLightSavingTimeInGivenDateTime(timeToConvertToUtc);
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
    ///     (for example in israel its 3 for gmt + 3 during summer)
    /// </param>
    /// <param name="isDayLightSavingTime">
    ///     True if its day light saving time right now in israel time zone,
    ///     if no value is given gets the time zone info from the system
    /// </param>
    /// <returns>The converted local value with <see cref="DateTimeKind.Unspecified" />.</returns>
    public static DateTime ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset(
        this DateTime utcTimeToConvert, int timeZoneOffsetSummerTime, bool? isDayLightSavingTime = null)
    {
        isDayLightSavingTime ??= IsDayLightSavingTimeInGivenDateTime(utcTimeToConvert);
        var dateTimeWithTimeZoneOffset =
            utcTimeToConvert + TimeSpan.FromHours(timeZoneOffsetSummerTime);

        if (timeZoneOffsetSummerTime != 0 && !isDayLightSavingTime.Value)
            dateTimeWithTimeZoneOffset -= TimeSpan.FromHours(1);

        return DateTime.SpecifyKind(dateTimeWithTimeZoneOffset, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// Determines whether the supplied date falls inside daylight-saving time in the Israel timezone.
    /// </summary>
    private static bool IsDayLightSavingTimeInGivenDateTime(this DateTime dateTime)
    {
        return TimeZoneInfo.FindSystemTimeZoneById(IsraelTimeZoneId).IsDaylightSavingTime(dateTime);
    }
}
