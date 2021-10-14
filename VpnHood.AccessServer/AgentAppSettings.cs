using System;

namespace VpnHood.AccessServer
{
    public class AgentAppSettings
    {
        public AgentAppSettings(Uri restBaseUrl, string restAuthorization, byte[] restSecret)
        {
            RestBaseUrl = restBaseUrl;
            RestSecret = restSecret;
            RestAuthorization = restAuthorization;
        }

        public Uri RestBaseUrl { get; set; }
        public byte[] RestSecret { get; set; }
        public string RestAuthorization { get; set; }
    }
}