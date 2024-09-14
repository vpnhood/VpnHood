using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.NetTester;

public class ClientOptions(string[] args)
{
    public int UpLength { get; } =  ArgumentUtils.Get(args, "/up", 20) * 1000000; // 10MB
    public int DownLength { get; } = ArgumentUtils.Get(args, "/down", 60) * 1000000; // 10MB
    public int TcpPort { get; } = ArgumentUtils.Get(args, "/tcp", 33700);
    public int HttpPort { get; } = ArgumentUtils.Get(args, "/http", 33700);
    public int ConnectionCount { get; } = ArgumentUtils.Get(args, "/multi", 10);

    [JsonConverter(typeof(IPEndPointConverter))]
    public IPEndPoint ServerEndPoint { get; } = ArgumentUtils.Get<IPEndPoint>(args, "/ep");
}