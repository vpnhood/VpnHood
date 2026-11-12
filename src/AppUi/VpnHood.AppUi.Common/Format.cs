using System.Globalization;

namespace VpnHood.AppUi.Common;

// The words a page composes from numbers: dates, ages, latencies, traffic.
//
// All of them follow CultureInfo.CurrentCulture - the device's REGION, which is not the language
// the user picked in the app. That is CurrentUICulture, and it chooses the words; the two are
// separate settings on every platform this ships to, so someone in Germany running the app in
// English still reads 05.01.2026 and 1,5 GB, the way the rest of their phone writes them.
//
// A hardcoded pattern is a hardcoded culture said less honestly, and it gets one group of users
// right at the expense of the rest: 05/01/2026 is the 5th of January to most of the world and the
// 1st of May to an American, and there is nothing on screen to say which was meant.

/// <summary>Provides the words a page composes from numbers: dates, ages, latencies and traffic.</summary>
/// <remarks>
/// Numbers and dates follow the device's region (<see cref="System.Globalization.CultureInfo.CurrentCulture"/>), not the
/// language picked in the app, as the rest of the device writes them.
/// </remarks>
public static class Format
{
    private const double Megabyte = 1_000_000;
    private const double Gigabyte = 1000 * Megabyte;

    /// <summary>Writes a date as the device writes dates, in local time, or a dash for none.</summary>
    /// <param name="date">The date, or <see langword="null"/>.</param>
    /// <returns>The date's words.</returns>
    public static string ShortDate(DateTime? date)
    {
        return date == null ? "-" : date.Value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    }

    /// <summary>Writes how long ago a time was, in the UI's words, as "3 minutes ago", or a dash for none.</summary>
    /// <param name="date">The time, or <see langword="null"/>.</param>
    /// <returns>The words.</returns>
    /// <exception cref="InvalidOperationException">The words have not been loaded (<see cref="Strings.InitAsync"/>).</exception>
    public static string RelativeTime(DateTime? date)
    {
        if (date == null)
            return "-";

        var strings = Strings.Current;
        var seconds = (long)Math.Floor((DateTime.UtcNow - date.Value.ToUniversalTime()).TotalSeconds);
        if (seconds < 5)
            return strings.TimeJustNow;
        if (seconds < 60)
            return strings.TimeSecondsAgo(seconds, strings.Seconds);

        var minutes = seconds / 60;
        if (minutes < 60)
            return strings.TimeMinutesAgo(minutes, minutes == 1 ? strings.Minute : strings.Minutes);

        var hours = minutes / 60;
        if (hours < 24)
            return strings.TimeHoursAgo(hours, hours == 1 ? strings.Hour : strings.Hours);

        var days = hours / 24;
        return strings.TimeDaysAgo(days, days == 1 ? strings.Day : strings.Days);
    }

    /// <summary>Writes a latency in milliseconds, as "123 ms", or a dash for none.</summary>
    /// <param name="latency">The latency, or <see langword="null"/>.</param>
    /// <returns>The words.</returns>
    public static string Latency(TimeSpan? latency)
    {
        return latency == null ? "-" : $"{(long)latency.Value.TotalMilliseconds} ms";
    }

    /// <summary>Writes an amount of traffic, as "1.5 GB" or "12 MB", in the device's number format.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The words.</returns>
    public static string Traffic(long bytes)
    {
        var isGb = bytes >= Gigabyte;
        var value = Math.Round(isGb ? bytes / Gigabyte : bytes / Megabyte, 1);
        return $"{value.ToString("0.#", CultureInfo.CurrentCulture)} {(isGb ? "GB" : "MB")}";
    }

    /// <summary>Writes an amount of traffic in the tighter form the home's circle shows, as "1.5GB".</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The words.</returns>
    public static string TrafficTight(long bytes)
    {
        return bytes >= Gigabyte
            ? (bytes / Gigabyte).ToString("0.#", CultureInfo.CurrentCulture) + "GB"
            : (bytes / Megabyte).ToString("0", CultureInfo.CurrentCulture) + "MB";
    }

    /// <summary>Writes the time left as hours, minutes and seconds, as "12:34:56"; a negative time is zero.</summary>
    /// <param name="remaining">The time left.</param>
    /// <returns>The words.</returns>
    public static string Countdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    /// <summary>
    /// Writes a price as a plan lists it: the store's currency symbol, then the amount in the device's number format.
    /// </summary>
    /// <param name="currencySymbol">The store's currency symbol.</param>
    /// <param name="amount">The amount.</param>
    /// <returns>The words.</returns>
    public static string Price(string currencySymbol, double amount)
    {
        return currencySymbol + amount.ToString("0.##", CultureInfo.CurrentCulture);
    }

    /// <summary>Writes a code in groups of four, joined by dashes, as a premium code is shown.</summary>
    /// <param name="code">The code.</param>
    /// <returns>The grouped code.</returns>
    public static string CodeGroups(string code)
    {
        var groups = Enumerable.Range(0, (code.Length + 3) / 4).Select(i => code.Substring(i * 4, Math.Min(4, code.Length - i * 4)));
        return string.Join("-", groups);
    }
}
