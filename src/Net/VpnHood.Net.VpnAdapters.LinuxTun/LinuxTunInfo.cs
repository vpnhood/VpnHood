namespace VpnHood.Net.VpnAdapters.LinuxTun;

// A tun as sysfs shows it: its name, the tag its creator wrote as its alias, and whether a
// process holds it open. The tun driver keeps the carrier up exactly while a queue is attached, so
// carrier 1 is a tun in use and 0 one that no process holds - a leftover. A tun that is down
// reports no carrier at all; that one is counted as held, since nothing proves it free, and only
// what is known to be free is ever deleted.
internal sealed record LinuxTunInfo(string Name, string Alias, bool IsHeld)
{
    private const string NetFolder = "/sys/class/net";

    public static bool InterfaceExists(string name)
    {
        return Directory.Exists(Path.Combine(NetFolder, name));
    }

    // Null when no interface has the name, or when the one that has it is not a tun.
    public static LinuxTunInfo? Find(string name)
    {
        var folder = Path.Combine(NetFolder, name);
        if (!File.Exists(Path.Combine(folder, "tun_flags")))
            return null;

        return new LinuxTunInfo(name, ReadAlias(folder), ReadIsHeld(folder));
    }

    // The entries are links into /sys/devices, so they are listed as entries, not as folders.
    public static IReadOnlyList<LinuxTunInfo> List()
    {
        return Directory.EnumerateFileSystemEntries(NetFolder)
            .Select(path => Find(Path.GetFileName(path)))
            .OfType<LinuxTunInfo>()
            .ToArray();
    }

    private static string ReadAlias(string folder)
    {
        var aliasFile = Path.Combine(folder, "ifalias");
        return File.Exists(aliasFile) ? File.ReadAllText(aliasFile).Trim() : "";
    }

    private static bool ReadIsHeld(string folder)
    {
        try {
            return File.ReadAllText(Path.Combine(folder, "carrier")).Trim() != "0";
        }
        catch (IOException) {
            // down (EINVAL), or gone since it was listed: not known to be free
            return true;
        }
    }
}
