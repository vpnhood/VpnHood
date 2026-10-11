using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VpnHood.AppUi.Hosting.Desktop.Windows;

// The machine PATH, with the install root on it so a command is typed by its name from any folder
// (desktop plan §6.4). Written here rather than by the installer, whose NSIS strings stop at 1024
// characters, and not through Environment.SetEnvironmentVariable, which writes the whole PATH back
// as plain text with %SystemRoot% expanded: the value stays expandable, the other entries as they
// were. Terminals opened afterwards see it; open ones do not.
internal static class WindowsMachinePath
{
    private const string EnvironmentKeyPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const string ValueName = "Path";

    // Adds the folder, unless the PATH names it already; true when it was added.
    public static bool Add(string folder)
    {
        using var key = OpenKey();
        var entries = ReadEntries(key);
        if (entries.Any(x => IsSameFolder(x, folder)))
            return false;

        Write(key, [.. entries, folder]);
        return true;
    }

    // Takes off every entry that names the folder, and only those; true when there was one.
    public static bool Remove(string folder)
    {
        using var key = OpenKey();
        var entries = ReadEntries(key);
        var kept = entries.Where(x => !IsSameFolder(x, folder)).ToArray();
        if (kept.Length == entries.Count)
            return false;

        Write(key, kept);
        return true;
    }

    private static RegistryKey OpenKey()
    {
        return Registry.LocalMachine.OpenSubKey(EnvironmentKeyPath, writable: true) ??
               throw new InvalidOperationException($@"Could not open HKLM\{EnvironmentKeyPath}.");
    }

    // as stored: %SystemRoot% and the like left unexpanded; empty entries dropped
    private static IReadOnlyList<string> ReadEntries(RegistryKey key)
    {
        var value = key.GetValue(ValueName, "", RegistryValueOptions.DoNotExpandEnvironmentNames) as string ?? "";
        return value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static void Write(RegistryKey key, IEnumerable<string> entries)
    {
        key.SetValue(ValueName, string.Join(';', entries), RegistryValueKind.ExpandString);

        // what Explorer, and so every terminal started from it, rereads its environment on
        SendMessageTimeout(HwndBroadcast, WmSettingChange, IntPtr.Zero, "Environment", SmtoAbortIfHung,
            5000, out _);
    }

    private static bool IsSameFolder(string entry, string folder)
    {
        static string Normalize(string path) =>
            Path.TrimEndingDirectorySeparator(Environment.ExpandEnvironmentVariables(path.Trim('"')));

        return string.Equals(Normalize(entry), Normalize(folder), StringComparison.OrdinalIgnoreCase);
    }

    private static readonly IntPtr HwndBroadcast = new(0xffff);
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, string lParam,
        uint flags, uint timeout, out IntPtr result);
}
