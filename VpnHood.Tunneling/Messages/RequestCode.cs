namespace VpnHood.Tunneling.Messages
{
    // [1B_version][1B_code] 

    public enum RequestCode : byte
    {
        Hello = 1,  // data: [4B_jsonLength][json_HelloRequest]
        TcpDatagramChannel = 2, // data: [4B_jsonLength][json_TcpDatagramChannelRequest]
        TcpProxyChannel = 3, // data: [4B_jsonLength][json_TcpProxyChannelRequest]
        SessionStatus = 4, // data: [4B_jsonLength][json_SessionRequest]
        UdpChannel = 5, // data: [4B_jsonLength][json_SessionRequest]
    }
}
