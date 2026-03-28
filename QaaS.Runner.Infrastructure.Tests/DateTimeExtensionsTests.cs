using System;
using NUnit.Framework;

namespace QaaS.Runner.Infrastructure.Tests;

public class DateTimeExtensionsTests
{
    [Test]
    public void ConvertDateTimeToUtcByTimeZoneOffset_WithDaylightSavingTime_DoesNotAdjustExtraHour()
    {
        var localTime = new DateTime(2025, 7, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var result = localTime.ConvertDateTimeToUtcByTimeZoneOffset(3, true);

        Assert.That(result, Is.EqualTo(new DateTime(2025, 7, 1, 9, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void ConvertDateTimeToUtcByTimeZoneOffset_WithoutDaylightSavingTime_AdjustsExtraHour()
    {
        var localTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var result = localTime.ConvertDateTimeToUtcByTimeZoneOffset(3, false);

        Assert.That(result, Is.EqualTo(new DateTime(2025, 1, 1, 10, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void ConvertDateTimeToUtcByTimeZoneOffset_WithZeroOffset_DoesNotAdjustExtraHour()
    {
        var localTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);

        var result = localTime.ConvertDateTimeToUtcByTimeZoneOffset(0, false);

        Assert.That(result, Is.EqualTo(new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void ConvertDateTimeToUtcByTimeZoneOffset_WithImplicitDaylightSavingTime_UsesDefaultTimeZone()
    {
        var localTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var isDst = GetDefaultTimeZone().IsDaylightSavingTime(localTime);
        var expected = localTime - TimeSpan.FromHours(3);
        if (!isDst)
            expected += TimeSpan.FromHours(1);
        expected = DateTime.SpecifyKind(expected, DateTimeKind.Utc);

        var result = localTime.ConvertDateTimeToUtcByTimeZoneOffset(3);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset_WithDaylightSavingTime_DoesNotAdjustExtraHour()
    {
        var utcTime = new DateTime(2025, 7, 1, 9, 0, 0, DateTimeKind.Utc);

        var result = utcTime.ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset(3, true);

        Assert.That(result, Is.EqualTo(new DateTime(2025, 7, 1, 12, 0, 0, DateTimeKind.Unspecified)));
    }

    [Test]
    public void ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset_WithoutDaylightSavingTime_AdjustsExtraHour()
    {
        var utcTime = new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        var result = utcTime.ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset(3, false);

        Assert.That(result, Is.EqualTo(new DateTime(2025, 1, 1, 11, 0, 0, DateTimeKind.Unspecified)));
    }

    [Test]
    public void ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset_WithZeroOffset_DoesNotAdjustExtraHour()
    {
        var utcTime = new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc);

        var result = utcTime.ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset(0, false);

        Assert.That(result, Is.EqualTo(new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Unspecified)));
    }

    [Test]
    public void ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset_WithImplicitDaylightSavingTime_UsesDefaultTimeZone()
    {
        var utcTime = new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var isDst = GetDefaultTimeZone().IsDaylightSavingTime(utcTime);
        var expected = utcTime + TimeSpan.FromHours(3);
        if (!isDst)
            expected -= TimeSpan.FromHours(1);
        expected = DateTime.SpecifyKind(expected, DateTimeKind.Unspecified);

        var result = utcTime.ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset(3);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ConvertDateTimeToUtcByTimeZoneOffset_WithCustomTimeZone_UsesProvidedRules()
    {
        const string timeZoneId = "Europe/London";
        var localTime = new DateTime(2025, 7, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var isDst = ResolveTimeZoneInfo(timeZoneId).IsDaylightSavingTime(localTime);
        var expected = localTime - TimeSpan.FromHours(1);
        if (!isDst)
            expected += TimeSpan.FromHours(1);
        expected = DateTime.SpecifyKind(expected, DateTimeKind.Utc);

        var result = localTime.ConvertDateTimeToUtcByTimeZoneOffset(1, timeZoneId: timeZoneId);

        Assert.That(result, Is.EqualTo(expected));
    }

    private static TimeZoneInfo GetDefaultTimeZone() => ResolveTimeZoneInfo(TimeZoneInfoResolver.DefaultTimeZoneId);

    private static TimeZoneInfo ResolveTimeZoneInfo(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsTimeZoneId))
                return TimeZoneInfo.FindSystemTimeZoneById(windowsTimeZoneId);

            throw;
        }
    }
}
