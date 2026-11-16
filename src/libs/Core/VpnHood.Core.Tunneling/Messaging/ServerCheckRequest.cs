using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Tunneling.Messaging;

/// <summary>
/// Represents a check that the server is there, which it answers as unauthorized without reading the request.
/// </summary>
public class ServerCheckRequest()
    : ClientRequest((byte)Messaging.RequestCode.ServerCheck);