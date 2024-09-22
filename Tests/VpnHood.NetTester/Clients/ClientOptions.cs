using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;
using VpnHood.NetTester.Utils;
using VpnHood.Server.Access;

namespace VpnHood.NetTester.Clients;

public class ClientOptions(string[] args)
{
    public int UpSize { get; } = ArgumentUtils.Get(args, "/up", 100);
    public int DownSize { get; } = ArgumentUtils.Get(args, "/down", 100);
    public int TcpPort { get; } = ArgumentUtils.Get(args, "/tcp", 33700);
    public int HttpPort { get; } = ArgumentUtils.Get(args, "/http", 8080);
    public int HttpsPort { get; } = ArgumentUtils.Get(args, "/https", 443);
    public int QuicPort { get; } = ArgumentUtils.Get(args, "/quic", 443);
    public int Timeout { get; } = ArgumentUtils.Get(args, "/timeout", 15);
    public bool Single { get; } = ArgumentUtils.Get(args, "/single", true);
    public int Multi { get; } = ArgumentUtils.Get(args, "/multi", 10);
    public Uri? Url { get; } = ArgumentUtils.Get<Uri?>(args, "/url", null);
    [JsonConverter(typeof(IPAddressConverter))]
    public IPAddress? UrlIp { get; } = ArgumentUtils.Get<IPAddress?>(args, "/url-ip", null);
    public string Domain { get; } = ArgumentUtils.Get(args, "/domain", CertificateUtil.CreateRandomDns());
    public bool IsValidDomain { get;} = args.Contains("/valid-domain", StringComparer.OrdinalIgnoreCase);
    public bool IsDebug { get; } = args.Contains("/debug", StringComparer.OrdinalIgnoreCase);

    [JsonConverter(typeof(IPEndPointConverter))]
    public IPEndPoint ServerEndPoint { get; } = ArgumentUtils.Get<IPEndPoint>(args, "/ep");

}