using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using VpnHood.AppUi.Hosting.Desktop.WindowsTun;

namespace VpnHood.AppUi.SmokeTest;

// The access key's link scheme that "service install" registers, under a made-up scheme, opened the way a browser
// opens a link. Elevated: the machine's classes are written.
[TestClass]
public class WindowsUrlSchemeTest
{
    private const string ClassesKeyPath = @"SOFTWARE\Classes";
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(20);

    [TestMethod]
    public void A_link_opens_with_its_command_whole_and_remove_leaves_nothing()
    {
        RequireElevation();
        var scheme = CreateScheme();
        var folder = Directory.CreateTempSubdirectory("vh-url-scheme-").FullName;
        try {
            // the open command writes the link it was given, as the window's --access-key takes it
            var marker = Path.Combine(folder, "opened.txt");
            var command = $"\"{Environment.SystemDirectory}\\cmd.exe\" /c echo \"%1\"> \"{marker}\"";
            Assert.IsTrue(WindowsUrlScheme.Add(scheme, "Test access key", Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                command));
            Assert.IsTrue(WindowsUrlScheme.Add(scheme, "Test access key", Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                command), "its own again, as a reinstall");

            // as long as a real key, past the 2083 characters of a browser's old URL limit
            var link = $"{scheme}://{new string('A', 2700)}+b/c==";
            Process.Start(new ProcessStartInfo(link) { UseShellExecute = true })?.Dispose();
            Assert.IsTrue(SpinWait.SpinUntil(() => ReadMarker(marker) != null, OpenTimeout), "the shell ran no command");
            Assert.AreEqual(link, ReadMarker(marker)?.Trim().Trim('"'));

            Assert.IsTrue(WindowsUrlScheme.Remove(scheme, command));
            Assert.IsFalse(HasKey(scheme));
            Assert.IsFalse(WindowsUrlScheme.Remove(scheme, command), "nothing was left to take off");
        }
        finally {
            DeleteKey(scheme);
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void A_scheme_another_app_holds_stays_its_after_add_and_remove()
    {
        RequireElevation();
        var scheme = CreateScheme();
        const string otherCommand = "other.exe \"%1\"";
        try {
            using (var commandKey = Registry.LocalMachine.CreateSubKey($@"{ClassesKeyPath}\{scheme}\shell\open\command"))
                commandKey.SetValue("", otherCommand);

            Assert.IsFalse(WindowsUrlScheme.Add(scheme, "Test access key", Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                "cmd.exe"));
            Assert.IsFalse(WindowsUrlScheme.Remove(scheme, "cmd.exe"));
            Assert.AreEqual(otherCommand, ReadCommand(scheme), "the other app keeps it");
        }
        finally {
            DeleteKey(scheme);
        }
    }

    private static string CreateScheme()
    {
        return "vhtest" + Guid.NewGuid().ToString("N")[..12];
    }

    private static string? ReadMarker(string path)
    {
        try {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (IOException) {
            return null; // still being written
        }
    }

    private static string? ReadCommand(string scheme)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{ClassesKeyPath}\{scheme}\shell\open\command");
        return key?.GetValue("") as string;
    }

    private static bool HasKey(string subKeyPath)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{ClassesKeyPath}\{subKeyPath}");
        return key != null;
    }

    private static void DeleteKey(string subKeyPath)
    {
        Registry.LocalMachine.DeleteSubKeyTree($@"{ClassesKeyPath}\{subKeyPath}", throwOnMissingSubKey: false);
    }

    private static void RequireElevation()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            Assert.Inconclusive("Run elevated: the machine's URL schemes are written under HKLM.");
    }
}
