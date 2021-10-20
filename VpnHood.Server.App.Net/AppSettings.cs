using System;
using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.Server.AccessServers;

namespace VpnHood.Server.App
{
    public class AppSettings
    {
        public RestAccessServerOptions? RestAccessServer { get; set; }
        public FileAccessServerOptions? FileAccessServer { get; set; }

        public byte[]? Secret { get; set; }
        public bool IsAnonymousTrackerEnabled { get; set; } = true;
        public bool IsDiagnoseMode { get; set; }
        public int OrgStreamReadBufferSize { get; set; } = new ServerOptions().OrgStreamReadBufferSize;
        public int TunnelStreamReadBufferSize { get; set; } = new ServerOptions().TunnelStreamReadBufferSize;
        public int MaxDatagramChannelCount { get; set; } = new ServerOptions().MaxDatagramChannelCount;

        [Obsolete("Deprecated from 2.2.276. Use FileAccessServer.TcpEndPoints")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IPEndPoint[]? EndPoints
        {
            get => null;
            set  
            {
                Console.WriteLine("Warning! Use FileAccessServer.TcpEndPoints in AppSettings instead of EndPoint. This will be deprecated soon!");
                FileAccessServer ??= new FileAccessServerOptions();
                FileAccessServer.TcpEndPoints = value!;
            }
        }

        [Obsolete("Deprecated from 1.4.2588. Use TcpEndPoint")]
        public int Port
        {
            get => 0;
            set
            {
                Console.WriteLine("Warning! Use FileAccessServer.TcpEndPoints in AppSettings instead of Port. This will be deprecated soon!");
                FileAccessServer ??= new FileAccessServerOptions();
                FileAccessServer.TcpEndPoints = new[] { new IPEndPoint(IPAddress.Any, value), new IPEndPoint(IPAddress.IPv6Any, value) };
            }
        }

        [Obsolete("Deprecated from 2.1.277. Use FileAccessServer.TcpEndPoints")]
        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint? EndPoint
        {
            get => null;
            set
            {
                Console.WriteLine("Warning! Use EndPoints in AppSettings instead of EndPoint. This will be deprecated soon!");
                FileAccessServer ??= new FileAccessServerOptions();
                FileAccessServer.TcpEndPoints = new[] { value! };
            }
        }
    }
}