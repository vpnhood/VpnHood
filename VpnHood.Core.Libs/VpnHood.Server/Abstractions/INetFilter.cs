﻿using System.Net;
using PacketDotNet;
using VpnHood.Common.Net;

namespace VpnHood.Server.Abstractions;

public interface INetFilter
{
    public IpRangeOrderedList BlockedIpRanges { get; set; }
    public IPPacket? ProcessRequest(IPPacket ipPacket);
    public IPEndPoint? ProcessRequest(ProtocolType protocol, IPEndPoint requestEndPoint);
    public IPPacket ProcessReply(IPPacket ipPacket);
}