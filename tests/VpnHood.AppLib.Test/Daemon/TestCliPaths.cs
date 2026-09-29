using VpnHood.AppUi.Hosting.Cli.Abstractions;

namespace VpnHood.AppLib.Test.Daemon;

internal sealed class TestCliPaths : IAppCliPaths
{
    public string InstanceName => "VpnHoodTest";
    public string CommandName => "vhtest";
    public string StoragePath => Path.Combine(Path.GetTempPath(), "VpnHoodTest", "storage");
    public string UiDataPath => Path.Combine(Path.GetTempPath(), "VpnHoodTest", "ui");
}
