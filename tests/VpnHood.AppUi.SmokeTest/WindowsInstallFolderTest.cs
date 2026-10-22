using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using VpnHood.AppUi.Hosting.Desktop.WindowsTun;

namespace VpnHood.AppUi.SmokeTest;

// Where "service install" may register the service from, read off real folders. Elevated: the
// folders only administrators can change are made under Program Files.
[TestClass]
public class WindowsInstallFolderTest
{
    [TestMethod]
    public void Folder_in_the_profile_is_refused()
    {
        var folder = Directory.CreateTempSubdirectory("vh-install-folder-").FullName;
        try {
            var problem = WindowsInstallFolder.FindProblem(folder);
            Assert.IsNotNull(problem, "the profile is its owner's to change");
            StringAssert.Contains(problem, Environment.UserName);
        }
        finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    // A file's attributes and extended attributes are the file's too.
    [TestMethod]
    [DataRow(FileSystemRights.Modify)]
    [DataRow(FileSystemRights.WriteAttributes)]
    [DataRow(FileSystemRights.WriteExtendedAttributes)]
    public void Folder_under_program_files_is_taken_until_another_may_change_a_file_in_it(FileSystemRights rights)
    {
        RequireElevation();
        var folder = CreateProgramFilesFolder();
        try {
            var file = Path.Combine(folder, "app.dll");
            File.WriteAllText(file, "");
            Assert.IsNull(WindowsInstallFolder.FindProblem(folder), "what Program Files passes down");

            var fileInfo = new FileInfo(file);
            var security = fileInfo.GetAccessControl();
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
                rights, AccessControlType.Allow));
            fileInfo.SetAccessControl(security);

            var problem = WindowsInstallFolder.FindProblem(folder);
            Assert.IsNotNull(problem, "Users may now change the file");
            StringAssert.Contains(problem, file);
        }
        finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    [TestMethod]
    public void Link_in_the_folder_is_refused()
    {
        RequireElevation();
        var folder = CreateProgramFilesFolder();
        var target = Directory.CreateTempSubdirectory("vh-install-folder-target-").FullName;
        try {
            var link = Path.Combine(folder, "lib");
            Directory.CreateSymbolicLink(link, target);

            var problem = WindowsInstallFolder.FindProblem(folder);
            Assert.IsNotNull(problem, "what the service loads through the link is elsewhere");
            StringAssert.Contains(problem, link);
        }
        finally {
            Directory.Delete(folder, recursive: true); // the link, not what it points at
            Directory.Delete(target, recursive: true);
        }
    }

    // An installer's root: the service runs the root's launcher, which runs the version "current"
    // names, a folder of the root's own (WindowsDesktopPaths.InstallRootPath).
    [TestMethod]
    public void Install_roots_current_naming_a_version_in_it_is_taken()
    {
        RequireElevation();
        var root = CreateProgramFilesFolder();
        try {
            var version = Path.Combine(root, "8.2.900");
            Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, "app.dll"), "");
            CreateJunction(Path.Combine(root, WindowsDesktopPaths.CurrentFolderName), version);

            Assert.IsNull(WindowsInstallFolder.FindProblem(root), "the junction and what it names are the root's");
        }
        finally {
            DeleteInstallRoot(root);
        }
    }

    [TestMethod]
    public void Install_roots_current_naming_a_folder_elsewhere_is_refused()
    {
        RequireElevation();
        var root = CreateProgramFilesFolder();
        var elsewhere = CreateProgramFilesFolder();
        try {
            var current = Path.Combine(root, WindowsDesktopPaths.CurrentFolderName);
            CreateJunction(current, elsewhere);

            var problem = WindowsInstallFolder.FindProblem(root);
            Assert.IsNotNull(problem, "what the service would load is not the root's");
            StringAssert.Contains(problem, current);
        }
        finally {
            DeleteInstallRoot(root);
            Directory.Delete(elsewhere, recursive: true);
        }
    }

    // The junction's own access list, not its target's: whoever may rewrite the link chooses the
    // version the service runs.
    [TestMethod]
    public void Install_roots_current_that_others_may_re_point_is_refused()
    {
        RequireElevation();
        var root = CreateProgramFilesFolder();
        try {
            var version = Path.Combine(root, "8.2.900");
            Directory.CreateDirectory(version);
            var current = Path.Combine(root, WindowsDesktopPaths.CurrentFolderName);
            CreateJunction(current, version);
            Run("icacls.exe", current, "/grant", "*S-1-5-32-545:(M)", "/L"); // Users, on the link itself

            var problem = WindowsInstallFolder.FindProblem(root);
            Assert.IsNotNull(problem, "Users may now re-point the junction");
            StringAssert.Contains(problem, current);
        }
        finally {
            DeleteInstallRoot(root);
        }
    }

    // The service is registered under the path it was started from: a link to a folder only
    // administrators can change is still whoever made the link's to point elsewhere.
    [TestMethod]
    public void Folder_reached_through_a_link_is_refused()
    {
        RequireElevation();
        var folder = CreateProgramFilesFolder();
        var link = Path.Combine(Path.GetTempPath(), $"vh-install-folder-link-{Guid.NewGuid():N}");
        try {
            Assert.IsNull(WindowsInstallFolder.FindProblem(folder), "the folder itself, by its own path");
            Directory.CreateSymbolicLink(link, folder);

            var problem = WindowsInstallFolder.FindProblem(link);
            Assert.IsNotNull(problem, "the link is in the profile, whose owner may point it elsewhere");
            StringAssert.Contains(problem, folder);
        }
        finally {
            Directory.Delete(link);
            Directory.Delete(folder, recursive: true);
        }
    }

    // In a folder that tells case apart, a link may lead to a name that differs only in case, which
    // compares equal to its target's.
    [TestMethod]
    public void Folder_reached_through_a_link_that_differs_only_in_case_is_refused()
    {
        RequireElevation();
        var parent = CreateProgramFilesFolder();
        try {
            SetCaseSensitive(parent);
            var folder = Path.Combine(parent, "VpnHood");
            Directory.CreateDirectory(folder);
            Assert.IsNull(WindowsInstallFolder.FindProblem(folder), "the folder itself, by its own path");

            var link = Path.Combine(parent, "vpnhood");
            Directory.CreateSymbolicLink(link, folder);

            var problem = WindowsInstallFolder.FindProblem(link);
            Assert.IsNotNull(problem, "whoever made the link may point it elsewhere");
            StringAssert.Contains(problem, link);
        }
        finally {
            Directory.Delete(parent, recursive: true);
        }
    }

    [TestMethod]
    public void Folder_owned_by_another_is_refused()
    {
        RequireElevation();
        var folder = CreateProgramFilesFolder();
        try {
            Assert.IsNull(WindowsInstallFolder.FindProblem(folder), "what Program Files passes down");

            // an owner may always rewrite the access list, whatever it says now
            using var identity = WindowsIdentity.GetCurrent();
            var folderInfo = new DirectoryInfo(folder);
            var security = folderInfo.GetAccessControl();
            security.SetOwner(identity.User ?? throw new InvalidOperationException("The test's user has no SID."));
            folderInfo.SetAccessControl(security);

            var problem = WindowsInstallFolder.FindProblem(folder);
            Assert.IsNotNull(problem, "the folder is now this user's own");
            StringAssert.Contains(problem, Environment.UserName);
        }
        finally {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static string CreateProgramFilesFolder()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            $"VpnHoodInstallFolderTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void SetCaseSensitive(string folder)
    {
        Run("fsutil.exe", "file", "setCaseSensitiveInfo", folder, "enable");
    }

    // The junction first, by itself: .NET's recursive delete cannot remove one ("The parameter is
    // incorrect"), as the uninstaller removes it first too.
    private static void DeleteInstallRoot(string root)
    {
        var current = Path.Combine(root, WindowsDesktopPaths.CurrentFolderName);
        if (Directory.Exists(current))
            Directory.Delete(current);

        Directory.Delete(root, recursive: true);
    }

    // a junction, as the installer makes "current" - Directory.CreateSymbolicLink makes a symbolic link
    private static void CreateJunction(string link, string target)
    {
        Run("cmd.exe", "/c", "mklink", "/J", link, target);
    }

    private static void Run(string fileName, params string[] arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(fileName, arguments) {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException($"{fileName} did not start.");
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.AreEqual(0, process.ExitCode, $"{fileName} {string.Join(' ', arguments)} failed: {output}");
    }

    private static void RequireElevation()
    {
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            Assert.Inconclusive("Run elevated: only an administrator makes a folder under Program Files.");
    }
}
