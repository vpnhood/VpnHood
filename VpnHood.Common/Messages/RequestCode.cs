namespace VpnHood.Messages
{
    // [1B_version][1B_code] 

    public enum RequestCode : byte
    {
        Hello = 1,  // data: [4B_jsonLength][json_HelloRequest]
        TcpDatagramChannel = 2, // data: [8B_sessionId]
        TcpProxyChannel = 3 // data: [4B_jsonLength][json_TcpProxyRequest]
    }
}
