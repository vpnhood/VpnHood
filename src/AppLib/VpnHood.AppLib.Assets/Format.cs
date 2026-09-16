using System.Globalization;

namespace VpnHood.AppLib.Assets;

// The web UI's Util, for the words a page composes from numbers: dates, ages, latencies, traffic.
public static class Format
{
    private const double Megabyte = 1_000_000;
    private const double Gigabyte = 1000 * Megabyte;

    // "Jan 5, 2026", or a dash for no date (Util.getShortDate)
    public static string ShortDate(DateTime? date)
    {
        return date == null ? "-" : date.Value.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));
    }

    // "05/01/2026" as the home circle shows an expiry (HomeConnectionInfo.getExpireDate)
    public static string ExpireDate(DateTime date)
    {
        return date.ToLocalTime().ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
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

    // "1.5GB" / "12MB", the tighter form of the home circle (HomeConnectionInfo.bandwidthUsage)
    public static string TrafficTight(long bytes)
    {
        return bytes >= Gigabyte
            ? (bytes / Gigabyte).ToString("0.#", CultureInfo.InvariantCulture) + "GB"
            : (bytes / Megabyte).ToString("0", CultureInfo.InvariantCulture) + "MB";
    }

    // "12:34:56" (CountDown.calcRemainingTime)
    public static string Countdown(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
    }

    // the price as the plan lists it: "$" then the number as the store gave it
    public static string Price(string currencySymbol, double amount)
    {
        return currencySymbol + amount.ToString("0.##", CultureInfo.InvariantCulture);
    }

    // "XXXX-XXXX-XXXX-XXXX-XXXX", the shape of a code (PremiumCodeDetails)
    public static string CodeGroups(string code)
    {
        var groups = Enumerable.Range(0, (code.Length + 3) / 4).Select(i => code.Substring(i * 4, Math.Min(4, code.Length - i * 4)));
        return string.Join("-", groups);
    }
}
