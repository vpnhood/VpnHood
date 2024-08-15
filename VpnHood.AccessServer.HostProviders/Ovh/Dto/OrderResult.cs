namespace VpnHood.AccessServer.HostProvider.Ovh.Dto;

internal class OrderResult
{
    public required string OldIp { get; set; }
    public required string NewIp { get; set; }
    public required bool CanBeTerminated { get; set; }
}