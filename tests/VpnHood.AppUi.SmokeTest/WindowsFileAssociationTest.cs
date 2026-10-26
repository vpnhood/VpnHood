using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using Microsoft.Win32;
using VpnHood.AppUi.Hosting.Desktop.WindowsTun;

namespace VpnHood.AppUi.SmokeTest;

// The access key's file type that "service install" registers, under a made-up extension and ProgID, opened the way
// Explorer opens a file. Elevated: the machine's classes are written.
[TestClass]
public class WindowsFileAssociationTest
{
    private const string ClassesKeyPath = @"SOFTWARE\Classes";
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(20);

    [TestMethod]
    public void A_file_of_the_type_opens_with_its_command_and_remove_leaves_nothing()
    {
        RequireElevation();
        var (extension, progId) = CreateNames();
        var folder = Directory.CreateTempSubdirectory("vh-file-association-").FullName;
        try {
            // the open command writes the path it was given, as the window's --access-key takes it
            var marker = Path.Combine(folder, "opened.txt");
            var command = $"\"{Environment.SystemDirectory}\\cmd.exe\" /c echo \"%1\"> \"{marker}\"";
            WindowsFileAssociation.Add(progId, "Test access key", Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                command, [extension]);

            Assert.AreEqual(progId, ReadDefault(extension), "no app held the extension, so it is the app's");
            Assert.IsTrue(IsOfferedUnderOpenWith(extension, progId));
            Assert.AreEqual(command, ReadDefault($@"{progId}\shell\open\command"));

            var file = Path.Combine(folder, "Test Server" + extension);
            File.WriteAllText(file, "vh:test");
            OpenWithShell(file);
            Assert.IsTrue(SpinWait.SpinUntil(() => ReadMarker(marker) != null, OpenTimeout), "the shell ran no command");
            StringAssert.Contains(ReadMarker(marker), file);

            Assert.IsTrue(WindowsFileAssociation.Remove(progId, [extension]));
            Assert.IsFalse(HasKey(progId));
            Assert.IsFalse(HasKey(extension), "an extension only the app gave the machine goes with it");
            Assert.IsFalse(WindowsFileAssociation.Remove(progId, [extension]), "nothing was left to take off");
        }
        finally {
            DeleteKey(progId);
            DeleteKey(extension);
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void An_extension_another_app_holds_stays_its_and_keeps_it_after_remove()
    {
        RequireElevation();
        var (extension, progId) = CreateNames();
        const string otherProgId = "VpnHoodTest.OtherApp";
        try {
            WriteDefault(extension, otherProgId);
            WindowsFileAssociation.Add(progId, "Test access key", Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                "cmd.exe", [extension]);
            Assert.AreEqual(otherProgId, ReadDefault(extension));
            Assert.IsTrue(IsOfferedUnderOpenWith(extension, progId), "offered under Open with all the same");

            Assert.IsTrue(WindowsFileAssociation.Remove(progId, [extension]));
            Assert.AreEqual(otherProgId, ReadDefault(extension), "the other app keeps it");
            Assert.IsFalse(IsOfferedUnderOpenWith(extension, progId));
        }
        finally {
            DeleteKey(progId);
            DeleteKey(extension);
        }
    }

    private static (string Extension, string ProgId) CreateNames()
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        return ($".vhtest{id}", $"VpnHoodTest{id}.AccessKey");
    }

    // as Explorer opens a file: ShellExecute's default verb
    private static void OpenWithShell(string file)
    {
        using var process = Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
    }

    private static void WriteDefault(string subKeyPath, string value)
    {
        using var key = Registry.LocalMachine.CreateSubKey($@"{ClassesKeyPath}\{subKeyPath}");
        key.SetValue("", value);
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

    private static string? ReadDefault(string subKeyPath)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{ClassesKeyPath}\{subKeyPath}");
        return key?.GetValue("") as string;
    }

    private static bool IsOfferedUnderOpenWith(string extension, string progId)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"{ClassesKeyPath}\{extension}\OpenWithProgids");
        return key?.GetValue(progId) != null;
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
            Assert.Inconclusive("Run elevated: the machine's file types are written under HKLM.");
    }
}
