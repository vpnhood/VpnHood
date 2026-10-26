using Microsoft.Win32;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// A URL scheme whose links open in an app, for the whole machine: the scheme's own class, with its
// name, its icon and its open command. Unlike an extension, a scheme has one handler, so it is taken
// only while no other app holds it, and taken off only while it is still this app's: the command
// tells whose it is.
internal static class WindowsUrlScheme
{
    private const string ClassesKeyPath = @"SOFTWARE\Classes";
    private const string CommandKeyPath = @"shell\open\command";

    // False when another app holds the scheme.
    public static bool Add(string scheme, string name, string iconPath, string openCommand)
    {
        using var classesKey = OpenClassesKey();
        if (ReadCommand(classesKey, scheme) is { } holder && holder != openCommand)
            return false;

        using var schemeKey = classesKey.CreateSubKey(scheme);
        schemeKey.SetValue("", $"URL:{name}");
        schemeKey.SetValue("URL Protocol", "");

        using var iconKey = schemeKey.CreateSubKey("DefaultIcon");
        iconKey.SetValue("", $"\"{iconPath}\",0");

        using var commandKey = schemeKey.CreateSubKey(CommandKeyPath);
        commandKey.SetValue("", openCommand);
        return true;
    }

    // True when the scheme was this app's and is taken off.
    public static bool Remove(string scheme, string openCommand)
    {
        using var classesKey = OpenClassesKey();
        if (ReadCommand(classesKey, scheme) != openCommand)
            return false;

        classesKey.DeleteSubKeyTree(scheme, throwOnMissingSubKey: false);
        return true;
    }

    private static RegistryKey OpenClassesKey()
    {
        return Registry.LocalMachine.OpenSubKey(ClassesKeyPath, writable: true) ??
               throw new InvalidOperationException($@"Could not open HKLM\{ClassesKeyPath}.");
    }

    private static string? ReadCommand(RegistryKey classesKey, string scheme)
    {
        using var commandKey = classesKey.OpenSubKey($@"{scheme}\{CommandKeyPath}");
        return commandKey?.GetValue("") as string;
    }
}
