using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// A file type that opens in an app, for the whole machine: the app's ProgID - the type's name, its icon,
// its open command - and each extension naming it. An extension's default is taken only while no other
// app holds it; the app is offered under "Open with" either way, and a person's own choice (Explorer's
// UserChoice) stays theirs. Explorer is told at once.
internal static class WindowsFileAssociation
{
    private const string ClassesKeyPath = @"SOFTWARE\Classes";
    private const string OpenWithProgIdsKeyName = "OpenWithProgids";

    public static void Add(string progId, string typeName, string iconPath, string openCommand,
        IReadOnlyList<string> extensions)
    {
        using var classesKey = OpenClassesKey();
        WriteProgId(classesKey, progId, typeName, iconPath, openCommand);
        foreach (var extension in extensions)
            AddExtension(classesKey, extension, progId);

        NotifyExplorer();
    }

    // Takes off the ProgID and what names it, and only that; true when there was any.
    public static bool Remove(string progId, IReadOnlyList<string> extensions)
    {
        using var classesKey = OpenClassesKey();
        var removed = HasSubKey(classesKey, progId);
        classesKey.DeleteSubKeyTree(progId, throwOnMissingSubKey: false);
        foreach (var extension in extensions)
            removed |= RemoveExtension(classesKey, extension, progId);

        if (removed)
            NotifyExplorer();

        return removed;
    }

    private static RegistryKey OpenClassesKey()
    {
        return Registry.LocalMachine.OpenSubKey(ClassesKeyPath, writable: true) ??
               throw new InvalidOperationException($@"Could not open HKLM\{ClassesKeyPath}.");
    }

    private static void WriteProgId(RegistryKey classesKey, string progId, string typeName, string iconPath,
        string openCommand)
    {
        using var progIdKey = classesKey.CreateSubKey(progId);
        progIdKey.SetValue("", typeName);

        using var iconKey = progIdKey.CreateSubKey("DefaultIcon");
        iconKey.SetValue("", $"\"{iconPath}\",0");

        using var commandKey = progIdKey.CreateSubKey(@"shell\open\command");
        commandKey.SetValue("", openCommand);
    }

    private static void AddExtension(RegistryKey classesKey, string extension, string progId)
    {
        using var extensionKey = classesKey.CreateSubKey(extension);
        using var openWithKey = extensionKey.CreateSubKey(OpenWithProgIdsKeyName);
        openWithKey.SetValue(progId, "");

        if (extensionKey.GetValue("") is not string { Length: > 0 } holder || holder == progId)
            extensionKey.SetValue("", progId);
    }

    private static bool RemoveExtension(RegistryKey classesKey, string extension, string progId)
    {
        using var extensionKey = classesKey.OpenSubKey(extension, writable: true);
        if (extensionKey == null)
            return false;

        var isDefault = extensionKey.GetValue("") as string == progId;
        if (isDefault)
            extensionKey.DeleteValue("");
        var isOffered = RemoveOpenWith(extensionKey, progId);

        // an extension only this app gave the machine goes with it
        if (IsEmpty(extensionKey, OpenWithProgIdsKeyName))
            classesKey.DeleteSubKeyTree(extension, throwOnMissingSubKey: false);

        return isDefault || isOffered;
    }

    private static bool RemoveOpenWith(RegistryKey extensionKey, string progId)
    {
        using var openWithKey = extensionKey.OpenSubKey(OpenWithProgIdsKeyName, writable: true);
        if (openWithKey?.GetValue(progId) == null)
            return false;

        openWithKey.DeleteValue(progId);
        return true;
    }

    private static bool HasSubKey(RegistryKey key, string name)
    {
        using var subKey = key.OpenSubKey(name);
        return subKey != null;
    }

    // no values, and no subkeys but an empty one of the given name
    private static bool IsEmpty(RegistryKey key, string allowedEmptySubKeyName)
    {
        if (key.ValueCount > 0)
            return false;

        var subKeyNames = key.GetSubKeyNames();
        if (subKeyNames.Length == 0)
            return true;

        if (subKeyNames is not [var name] || !string.Equals(name, allowedEmptySubKeyName, StringComparison.OrdinalIgnoreCase))
            return false;

        using var subKey = key.OpenSubKey(name);
        return subKey is { ValueCount: 0, SubKeyCount: 0 };
    }

    private static void NotifyExplorer()
    {
        SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
    }

    private const int ShcneAssocChanged = 0x08000000;
    private const uint ShcnfIdList = 0x0000;

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
