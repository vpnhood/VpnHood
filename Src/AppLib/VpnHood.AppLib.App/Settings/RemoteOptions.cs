namespace VpnHood.AppLib.Settings;

public class RemoteOptions
{
    public Uri? Url { get; init; }
    public required RemoteSettings Default{ get; init; }
}