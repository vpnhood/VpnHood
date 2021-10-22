using System;
using System.Text.Json.Serialization;

namespace VpnHood.Server.AccessServers
{
    public class RestAccessServerOptions
    {
        public RestAccessServerOptions(string baseUrl, string authorization)
        {
            if (string.IsNullOrEmpty(baseUrl)) throw new ArgumentNullException(nameof(baseUrl));
            if (string.IsNullOrEmpty(authorization)) throw new ArgumentNullException(nameof(authorization));

            BaseUrl = baseUrl;
            Authorization = authorization;
        }

        public string BaseUrl { get; set; }
        public string Authorization { get; set; }
        
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CertificateThumbprint { get; set; }
    }
}