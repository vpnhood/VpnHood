using System;

namespace VpnHood.Client.App
{
    public class AppFeatures
    {
        public string Version => typeof(VpnHoodApp).Assembly.GetName().Version.ToString(3);
        public Guid? TestServerTokenId { get; internal set; }
        public bool IsExcludeAppsSupported { get; internal set; }
        public bool IsIncludeAppsSupported { get; internal set; }
    }
}
