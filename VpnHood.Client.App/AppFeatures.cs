using System;

namespace VpnHood.Client.App;

public class AppFeatures
{
    public Version Version { get; } = typeof(VpnHoodApp).Assembly.GetName().Version;
    public Guid? TestServerTokenId { get; internal set; }
    public bool IsExcludeAppsSupported { get; internal set; }
    public bool IsIncludeAppsSupported { get; internal set; }
    public Uri? UpdateInfoUrl { get; internal set; }
}