using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Server.App
{
    public class AppSettings
    {
        public Uri? RestBaseUrl { get; set; }
        public string? RestAuthorization { get; set; }
        public string? RestCertificateThumbprint { get; set; }

        [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
        public IPEndPoint[] EndPoints { get; set; } = { new(IPAddress.Any, 443), new(IPAddress.IPv6Any, 443) };
        public int UdpPort { get; set; }
        public bool IsAnonymousTrackerEnabled { get; set; } = true;
        public string? SslCertificatesPassword { get; set; }
        public bool IsDiagnoseMode { get; set; }
        public int OrgStreamReadBufferSize { get; set; } = new ServerOptions().OrgStreamReadBufferSize;
        public int TunnelStreamReadBufferSize { get; set; } = new ServerOptions().TunnelStreamReadBufferSize;
        public int MaxDatagramChannelCount { get; set; } = new ServerOptions().MaxDatagramChannelCount;

        [Obsolete("Deprecated from 1.4.2588. Use EndPoint")]
        public int Port
        {
            get => 0;
            set
            {
                Console.WriteLine("Warning! Use EndPoints in AppSettings instead of Port. This will be deprecated soon!");
                EndPoints = new[] { new IPEndPoint(IPAddress.Any, value), new IPEndPoint(IPAddress.IPv6Any, value) };
            }
        }

        [Obsolete("Deprecated from 2.1.277. Use EndPoints")]
        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint? EndPoint
        {
            get => null;
            set
            {
                Console.WriteLine("Warning! Use EndPoints in AppSettings instead of EndPoint. This will be deprecated soon!");
                EndPoints = new[] { value! };
            }
        }

    }
}