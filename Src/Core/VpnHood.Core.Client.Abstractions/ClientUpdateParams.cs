namespace VpnHood.Core.Client.Abstractions;

public class ClientUpdateParams
{
    public bool UseTcpOverTun { get; set; }
    public bool UseUdpChannel { get; set; }
    public bool DropUdp { get; set; }
    public bool DropQuic { get; set; }
}