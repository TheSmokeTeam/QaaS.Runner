using System;
using System.IO;
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
    public void ConvertDateTimeToUtcByTimeZoneOffset_WithImplicitDaylightSavingTime_UsesIsraelTimeZone()
    {
        var localTime = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Unspecified);
        var isDst = GetIsraelTimeZone().IsDaylightSavingTime(localTime);
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
    public void ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset_WithImplicitDaylightSavingTime_UsesIsraelTimeZone()
    {
        var utcTime = new DateTime(2025, 1, 1, 9, 0, 0, DateTimeKind.Utc);
        var isDst = GetIsraelTimeZone().IsDaylightSavingTime(utcTime);
        var expected = utcTime + TimeSpan.FromHours(3);
        if (!isDst)
            expected -= TimeSpan.FromHours(1);
        expected = DateTime.SpecifyKind(expected, DateTimeKind.Unspecified);

        var result = utcTime.ConvertDateTimeFromUtcToTimeZoneByTimeZoneOffset(3);

        Assert.That(result, Is.EqualTo(expected));
    }

    private static TimeZoneInfo GetIsraelTimeZone()
    {
        var timezoneId = Environment.OSVersion.Platform == PlatformID.Unix
            ? Path.Join("Asia", "Jerusalem")
            : "Israel Standard Time";
        return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
    }
}
