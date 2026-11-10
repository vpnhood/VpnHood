using System.Globalization;

namespace VpnHood.Test;

// Machine-wide increasing counters. Per-process static counters restart at the same base in
// every test host, so parallel hosts walk identical sequences and race on the same endpoints;
// sharing the counter via a mutex-protected temp file makes every allocation unique across
// processes.
public static class CrossProcessCounter
{
    public static int Next(string name, int first, int last)
    {
        using var mutex = new Mutex(initiallyOwned: false, $"VpnHood.Test.Counter.{name}");
        try {
            mutex.WaitOne();
        }
        catch (AbandonedMutexException) {
            // a test host died while holding the mutex; ownership is transferred to us
        }

        try {
            // In the user's folder, which TMP does not move: a run that points TMP at a folder of its own, to keep
            // its working folders from another run's cleanup, would otherwise walk a counter of its own, and its
            // test hosts would take the ports and IP blocks of the runs beside it. Not an assembly's working
            // folder either: an assembly cleanup must not reset a counter another test host is still walking.
            var userFolderPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var folderPath = Path.Combine(userFolderPath.Length > 0 ? userFolderPath : Path.GetTempPath(), "VpnHood.Test");
            Directory.CreateDirectory(folderPath);
            var filePath = Path.Combine(folderPath, $"VpnHood.Test.Counter.{name}.txt");
            var value = first;
            try {
                value = int.Parse(File.ReadAllText(filePath), CultureInfo.InvariantCulture) + 1;
            }
            catch {
                // missing or corrupted counter file; restart from first
            }

            if (value < first || value > last)
                value = first;

            File.WriteAllText(filePath, value.ToString(CultureInfo.InvariantCulture));
            return value;
        }
        finally {
            mutex.ReleaseMutex();
        }
    }
}
