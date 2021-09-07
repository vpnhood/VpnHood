using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server.App
{
    public class AppSettings
    {
        public Guid? ServerId { get; set; }
        public Uri? RestBaseUrl { get; set; }
        public string? RestAuthorization { get; set; }
        public string? RestCertificateThumbprint { get; set; }

        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint EndPoint { get; set; } = new(IPAddress.Any, 443);

        public bool IsAnonymousTrackerEnabled { get; set; } = true;
        public string? SslCertificatesPassword { get; set; }
        public bool IsDiagnoseMode { get; set; }
        public int OrgStreamReadBufferSize { get; set; } = new ServerOptions().OrgStreamReadBufferSize;
        public int TunnelStreamReadBufferSize { get; set; } = new ServerOptions().TunnelStreamReadBufferSize;
        public int MaxDatagramChannelCount { get; set; } = new ServerOptions().MaxDatagramChannelCount;

        [Obsolete("Deprecated from 1.4.2588. Use ListenerEndPoint")]
        public int Port
        {
            get => EndPoint.Port;
            set => EndPoint.Port = value;
        }
    }
}