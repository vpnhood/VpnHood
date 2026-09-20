using System.Globalization;

namespace VpnHood.AppUi.Services;

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
public static class Format
{
    private const double Megabyte = 1_000_000;
    private const double Gigabyte = 1000 * Megabyte;

    // The date as this device writes dates - 1/5/2026, 05.01.2026, 2026-01-05 - or a dash for no
    // date. The PATTERN is the culture's, not a pattern of ours with its month names translated:
    // which of the day and the month comes first is the whole point.
    public static string ShortDate(DateTime? date)
    {
        return date == null ? "-" : date.Value.ToLocalTime().ToString("d", CultureInfo.CurrentCulture);
    }

    // "3 minutes ago", in the app's words (Util.getRelativeTime)
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

    // "123 ms" (Util.formatLatency)
    public static string Latency(TimeSpan? latency)
    {
        return latency == null ? "-" : $"{(long)latency.Value.TotalMilliseconds} ms";
    }

    // "1.5 GB" or "12 MB" (statistics.vue's calcUnit)
    public static string Traffic(long bytes)
    {
        var isGb = bytes >= Gigabyte;
        var value = Math.Round(isGb ? bytes / Gigabyte : bytes / Megabyte, 1);
        return $"{value.ToString("0.#", CultureInfo.CurrentCulture)} {(isGb ? "GB" : "MB")}";
    }

    // "1.5GB" / "12MB", the tighter form the home circle shows - the same number as Traffic above,
    // so it has to be written the same way: a reader who sees 1,5 GB on one screen must not see
    // 1.5GB on the other.
    public static string TrafficTight(long bytes)
    {
        return bytes >= Gigabyte
            ? (bytes / Gigabyte).ToString("0.#", CultureInfo.CurrentCulture) + "GB"
            : (bytes / Megabyte).ToString("0", CultureInfo.CurrentCulture) + "MB";
    }

    // "12:34:56" (CountDown.calcRemainingTime)
    public static string Countdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    // The price as the plan lists it: the store's own currency symbol, then the amount written the
    // way this device writes numbers. The symbol is the store's because only the store knows what
    // the user is being charged in; the decimal separator is the reader's.
    public static string Price(string currencySymbol, double amount)
    {
        return currencySymbol + amount.ToString("0.##", CultureInfo.CurrentCulture);
    }

    // "XXXX-XXXX-XXXX-XXXX-XXXX", the shape of a code (PremiumCodeDetails)
    public static string CodeGroups(string code)
    {
        var groups = Enumerable.Range(0, (code.Length + 3) / 4).Select(i => code.Substring(i * 4, Math.Min(4, code.Length - i * 4)));
        return string.Join("-", groups);
    }
}
