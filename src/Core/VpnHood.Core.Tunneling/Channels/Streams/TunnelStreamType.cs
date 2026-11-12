using System.Text.Json.Serialization;

namespace VpnHood.Core.Tunneling.Channels.Streams;

/// <summary>
/// Specifies how a client's stream to the server is framed: as its request's <c>X-BinaryStream</c> header names it, or
/// <see cref="WebSocket"/> for a WebSocket upgrade, whatever the header says.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TunnelStreamType>))]
public enum TunnelStreamType
{
    /// <summary>A type the server does not know, which it does not take.</summary>
    Unknown,

    /// <summary>A plain stream after an HTTP POST, which carries one request.</summary>
    None,

    /// <summary>A type that nothing uses.</summary>
    Standard,

    /// <summary>A WebSocket stream after a WebSocket upgrade, which may carry one request after another.</summary>
    WebSocket,

    /// <summary>
    /// A stream in WebSocket frames after an HTTP POST, with no upgrade, which may carry one request after another.
    /// </summary>
    WebSocketNoHandshake
}
