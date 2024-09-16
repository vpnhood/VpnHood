using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.NetTester.Utils;

namespace VpnHood.NetTester.Clients;

public class ClientOptions(string[] args)
{
    public int UpLength { get; } = ArgumentUtils.Get(args, "/up", 60) * 1000000; // 6MB
    public int DownLength { get; } = ArgumentUtils.Get(args, "/down", 60) * 1000000; // 60MB
    public int TcpPort { get; } = ArgumentUtils.Get(args, "/tcp", 33700);
    public int HttpPort { get; } = ArgumentUtils.Get(args, "/http", 8080);
    public int HttpsPort { get; } = ArgumentUtils.Get(args, "/https", 443);
    public bool Single { get; } = ArgumentUtils.Get(args, "/single", true);
    public int Multi { get; } = ArgumentUtils.Get(args, "/multi", 10);
    public Uri? Url { get; } = ArgumentUtils.Get<Uri?>(args, "/url", null);
    public string? Domain { get; } = ArgumentUtils.Get<string?>(args, "/domain", null);
    public bool IsValidDomain { get;} = ArgumentUtils.Get(args, "/valid-domain", false);
    public bool IsDebug { get; } = args.Contains("/debug", StringComparer.OrdinalIgnoreCase);

    [JsonConverter(typeof(IPEndPointConverter))]
    public IPEndPoint ServerEndPoint { get; } = ArgumentUtils.Get<IPEndPoint>(args, "/ep");

}