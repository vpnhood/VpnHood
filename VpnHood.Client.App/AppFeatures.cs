using System;

namespace VpnHood.Client.App
{
    public class AppFeatures
    {
        public string Version => typeof(VpnHoodApp).Assembly.GetName().Version.ToString(3);
        public Guid? TestServerTokenId { get; internal set; }
        public bool IsExcludeApplicationsSupported { get; internal set; }
        public bool IsIncludeApplicationsSupported { get; internal set; }
        public bool IsExcludeNetworksSupported { get; internal set; }
        public bool IsIncludeNetworksSupported { get; internal set; }
    }
}
