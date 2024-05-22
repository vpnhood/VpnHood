namespace VpnHood.Tunneling.Messaging;
// [1B_version][1B_code][4B_jsonLength][json_request]

public enum RequestCode : byte 
{
    Hello = 1, 
    TcpDatagramChannel = 2,
    StreamProxyChannel = 3,
    // SessionStatus = 4,
    UdpPacket = 5,
    AdReward = 10,
    ServerStatus = 20,
    Bye = 50
}