using System.Text.Json.Serialization;

namespace VpnHood.Core.Tunneling.Channels.Streams;

/// <summary>
/// Specifies how a client's stream to the server is framed: as its request's <c>X-BinaryStream</c> header names it, or
/// <see cref="WebSocket"/> for a WebSocket upgrade, whatever the header says.
/// </summary>
// The numbers are fixed, as the header may name a type by its number: 2 was a type no version used, which the server
// now takes for one it does not know
[JsonConverter(typeof(JsonStringEnumConverter<TunnelStreamType>))]
public enum TunnelStreamType
{
    /// <summary>A type the server does not know, which it does not take.</summary>
    Unknown = 0,

    /// <summary>A plain stream after an HTTP POST, which carries one request.</summary>
    None = 1,

    /// <summary>A WebSocket stream after a WebSocket upgrade, which may carry one request after another.</summary>
    WebSocket = 3,

    /// <summary>
    /// A stream in WebSocket frames after an HTTP POST, with no upgrade, which may carry one request after another.
    /// </summary>
    WebSocketNoHandshake = 4
}
