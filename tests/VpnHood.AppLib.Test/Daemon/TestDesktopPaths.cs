using VpnHood.AppUi.Hosting.Desktop.Platform;
using VpnHood.Test;

namespace VpnHood.AppLib.Test.Daemon;

// instanceName: a name of the test's own where the name is machine-wide, as a window's pipe is.
internal sealed class TestDesktopPaths(string instanceName = "VpnHoodTest") : IAppDesktopPaths
{
    public string InstanceName => instanceName;
    public string CommandName => "vhtest";
    public bool IsStartedAsCommand => false;
    public string StoragePath => Path.Combine(TestHelper.AssemblyWorkingPath, InstanceName, "storage");
    public string UiDataPath => Path.Combine(TestHelper.AssemblyWorkingPath, InstanceName, "ui");
}
