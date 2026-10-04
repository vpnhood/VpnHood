using VpnHood.AppUi.Hosting.Cli.Abstractions;
using VpnHood.Test;

namespace VpnHood.AppLib.Test.Daemon;

internal sealed class TestCliPaths : IAppCliPaths
{
    public string InstanceName => "VpnHoodTest";
    public string CommandName => "vhtest";
    public string StoragePath => Path.Combine(TestHelper.AssemblyWorkingPath, InstanceName, "storage");
    public string UiDataPath => Path.Combine(TestHelper.AssemblyWorkingPath, InstanceName, "ui");
}
