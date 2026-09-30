using System.Reflection;
using VpnHood.AppUi.Hosting.Cli.Abstractions;

namespace VpnHood.AppUi.Hosting.Cli.Windows;

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
public class WindowsCliPaths(string appId) : IAppCliPaths
{
    public string InstanceName { get; } =
        Assembly.GetEntryAssembly()?.GetName().Name ??
        throw new InvalidOperationException("The entry assembly has no name, so this install has none.");

    // Nothing of this install is on the PATH yet, so a hint names the executable itself.
    public string CommandName => InstanceName;

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

    // What the service runs and an elevated copy of a command is started as: the apphost beside the
    // entry assembly, even when this process is dotnet.
    public string ExecutablePath => Path.Combine(AppContext.BaseDirectory, InstanceName + ".exe");
}
