using VpnHood.AppUi.Hosting.Desktop.Daemon.Platform;
using VpnHood.Test;

namespace VpnHood.AppLib.Test.Daemon;

// instanceName: a name of the test's own where the name is machine-wide, as a window's pipe is; the app id is
// the instance's name unless a test gives one of its own.
internal sealed class TestDesktopPaths(string instanceName = "VpnHoodTest", string? appId = null) : IAppDesktopPaths
{
    public string AppId => appId ?? instanceName;
    public string InstanceName => instanceName;
    public string CommandName => "vhtest";
    public bool IsStartedAsCommand => false;
    public string StoragePath => Path.Combine(TestHelper.AssemblyWorkingPath, InstanceName, "storage");
    public string UiDataPath => Path.Combine(TestHelper.AssemblyWorkingPath, InstanceName, "ui");
    public string? KillSwitchOffScriptPath => null;
}
