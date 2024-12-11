﻿using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Tunneling.Messaging;

public class StreamProxyChannelRequest()
    : RequestBase(Messaging.RequestCode.StreamProxyChannel)
{
    [JsonConverter(typeof(IPEndPointConverter))]
    public required IPEndPoint DestinationEndPoint { get; set; }

    public required byte[] CipherKey { get; init; }
    public required long CipherLength { get; init; }
}