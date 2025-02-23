namespace VpnHood.Core.Client.VpnServices.Abstractions;

public class ClientReconfigureParams
{
    public bool UseTcpOverTun { get; set; }
    public bool UseUdpChannel { get; set; }
    public bool DropUdp { get; set; }
    public bool DropQuic { get; set; }
}