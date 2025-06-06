﻿namespace VpnHood.Core.Tunneling.Messaging;
// [1B_version][1B_code][4B_jsonLength][json_request]

public enum RequestCode : byte
{
    Hello = 1,
    TcpPacketChannel = 2,
    ProxyChannel = 3,
    SessionStatus = 4,
    UdpPacket = 5,
    RewardedAd = 10,
    ServerStatus = 20, //todo: deprecated
    ServerCheck = 30,
    Bye = 50
}