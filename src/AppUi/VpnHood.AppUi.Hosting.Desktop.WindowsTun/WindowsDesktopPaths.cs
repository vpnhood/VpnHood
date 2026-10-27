using System.Reflection;
using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;

namespace VpnHood.AppUi.Hosting.Desktop.WindowsTun;

// Where a Windows install keeps things, and why they are not all one folder.
//
// The service runs as LocalSystem and owns its storage under ProgramData - the settings, the profiles,
// the log - which it keeps to itself and administrators (WindowsServiceStorage). The window and the
// commands run as whoever is signed in, unelevated, and may not open it, so what the UI extracts or
// keeps for itself goes under that person's local app data.
//
// The folders are named by the app id: no other app on the machine has it, and a Debug build's is
// not a release's. The instance - the service, the executable - is named after the entry assembly
// rather than the process, because the same install runs as several processes: the service and the
// window run the apphost, "dotnet VpnHoodClient.dll" runs dotnet.
public class WindowsDesktopPaths(string appId) : IAppDesktopPaths
{
    public string InstanceName { get; } =
        Assembly.GetEntryAssembly()?.GetName().Name ??
        throw new InvalidOperationException("The entry assembly has no name, so this install has none.");

    // The console launcher beside the window's executable, which a terminal waits for: the name the
    // app's identity writes into its assembly (VhAppCommandName). An app built without that identity
    // has only the window's executable, so a hint names that.
    public string CommandName => string.IsNullOrEmpty(_commandName) ? InstanceName : _commandName;

    private readonly string? _commandName = Assembly.GetEntryAssembly()?
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(x => x.Key == "VhAppCommandName")?.Value;

    public bool IsStartedAsCommand =>
        !string.IsNullOrEmpty(_commandName) &&
        string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), _commandName,
            StringComparison.OrdinalIgnoreCase);

    // The folder in use: the one an earlier start recorded when it had to pass over its own, else
    // its own.
    public string StoragePath => WindowsServiceStorage.ReadRecorded(RegistryKeyPath) ?? DefaultStoragePath;

    public string DefaultStoragePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), appId);

    public string UiDataPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), appId);

    // The app's own key under HKLM, which only administrators write and anyone reads: where the
    // service records what a client must look up before it can ask it anything - the channel's name -
    // and the storage folder it went to instead of its own.
    public string RegistryKeyPath => @"SOFTWARE\" + appId;

    // The install's own folder above the version's, when an installer made one: it holds the root's
    // launchers, which run the version that its "current" junction names (CurrentFolderName). Each
    // version has a folder of its own beside it, so an update only re-points the junction. Null for a
    // copy run where it lies - a zip, a debugger's build.
    public string? InstallRootPath {
        get {
            var root = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
            return root != null &&
                   File.Exists(Path.Combine(root, InstanceName + ".exe")) &&
                   Directory.Exists(Path.Combine(root, CurrentFolderName))
                ? root
                : null;
        }
    }

    // The junction in the install root that names the version in use; the build's root launchers
    // (VpnHood.AppUi.Hosting.Desktop.WindowsTun.targets) and the installer name it too.
    public const string CurrentFolderName = "current";

    // The root's window launcher, over "current".
    public string? InstalledLauncherPath =>
        InstallRootPath is { } installRootPath ? Path.Combine(installRootPath, InstanceName + ".exe") : null;

    // What the service runs and an elevated copy of a command is started as: the install root's
    // launcher, which an update never moves, else the apphost beside the entry assembly, even when
    // this process is dotnet.
    public string ExecutablePath =>
        Path.Combine(InstallRootPath ?? AppContext.BaseDirectory, InstanceName + ".exe");
}
