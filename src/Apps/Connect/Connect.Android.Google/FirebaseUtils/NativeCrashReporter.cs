using System.Text;
using Android.Content;
using Firebase.Crashlytics;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.App.Connect.Android.Google.FirebaseUtils;

// Reports the native crashes Android recorded for the app's processes since the last report: to the
// log, and to Crashlytics while the person shares usage data. Crashlytics catches only Java and managed
// crashes; a native one - the .NET runtime's own fatal errors, which abort the process - reaches only
// Play, whose report leaves out the abort message that says why.
public static class NativeCrashReporter
{
    private const string PreferencesName = "vh_native_crash_reporter";
    private const string LastReportedKey = "last_reported_time";

    // abort_message in Android's tombstone.proto: a string at the message's top level
    private const int AbortMessageField = 14;

    public static void Report()
    {
        try {
            ReportInternal();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not report the app's last native crashes.");
        }
    }

    private static void ReportInternal()
    {
        // Android records a process's end from 11 (API 30), and gives a native crash's tombstone from 12
        if (!OperatingSystem.IsAndroidVersionAtLeast(30))
            return;

        var context = Application.Context;
        var activityManager = (ActivityManager?)context.GetSystemService(Context.ActivityService)
            ?? throw new InvalidOperationException("Android gave no activity manager.");
        var preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)
            ?? throw new InvalidOperationException("Android gave no preferences for the crash reporter.");

        var lastReported = preferences.GetLong(LastReportedKey, 0);
        var crashes = activityManager.GetHistoricalProcessExitReasons(packageName: null, pid: 0, maxNum: 0)
            .Where(x => (ApplicationExitInfoReason)x.Reason == ApplicationExitInfoReason.CrashNative &&
                        x.Timestamp > lastReported)
            .OrderBy(x => x.Timestamp)
            .ToArray();

        if (crashes.Length == 0)
            return;

        // the switch as the app has just applied it; a crash found while it is off is never sent, as
        // Crashlytics' own
        var isEnabled = FirebaseAnalyticsTracker.IsInit && FirebaseAnalyticsTracker.Instance.IsEnabled;
        foreach (var crash in crashes) {
            var message = BuildMessage(crash);
            VhLogger.Instance.LogWarning("A process of the app ended in a native crash. {Message}", message);
            if (isEnabled)
                FirebaseCrashlytics.Instance.RecordException(new Java.Lang.Exception(message));
        }

        preferences.Edit()?.PutLong(LastReportedKey, crashes[^1].Timestamp)?.Apply();
    }

    private static string BuildMessage(ApplicationExitInfo crash)
    {
        var abortMessage = OperatingSystem.IsAndroidVersionAtLeast(31) ? TryReadAbortMessage(crash) : null;
        var time = DateTimeOffset.FromUnixTimeMilliseconds(crash.Timestamp);

        // the memory the process held, where Android recorded it (it may not for a crash)
        var memory = crash.Rss > 0 ? $", {crash.Rss / 1024} MB resident" : "";
        return $"Native crash of {crash.ProcessName} by signal {crash.Status} at {time:u}{memory}: " +
               $"{abortMessage ?? "no abort message"}";
    }

    private static string? TryReadAbortMessage(ApplicationExitInfo crash)
    {
        using var stream = crash.TraceInputStream;
        if (stream == null)
            return null;

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return ReadStringField(memory.GetBuffer().AsSpan(0, (int)memory.Length), AbortMessageField);
    }

    // A protobuf message's string field at its top level, the other fields skipped by their wire type.
    private static string? ReadStringField(ReadOnlySpan<byte> data, int fieldNumber)
    {
        var position = 0;
        while (position < data.Length) {
            var key = ReadVarint(data, ref position);
            switch (key & 7) {
                case 0:
                    ReadVarint(data, ref position);
                    break;
                case 1:
                    position += 8;
                    break;
                case 2:
                    var length = (int)ReadVarint(data, ref position);
                    if ((int)(key >> 3) == fieldNumber)
                        return Encoding.UTF8.GetString(data.Slice(position, length));
                    position += length;
                    break;
                case 5:
                    position += 4;
                    break;
                default:
                    throw new InvalidDataException($"The tombstone has a field of wire type {key & 7}.");
            }
        }

        return null;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> data, ref int position)
    {
        var value = 0UL;
        for (var shift = 0; shift < 64; shift += 7) {
            var b = data[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
                return value;
        }

        throw new InvalidDataException("The tombstone has a varint longer than 64 bits.");
    }
}
