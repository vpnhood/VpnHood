namespace VpnHood.AccessServer.Dtos;

public enum TrackClientRequest
{
    Nothing,
    LocalPort,
    LocalPortAndDstPort,
    LocalPortAndDstPortAndDstIp,
}