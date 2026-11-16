namespace VpnHood.Core.Tunneling.Messaging;

/// <summary>Represents a request for the session's status, which the server answers with the session's response.</summary>
public class SessionStatusRequest()
    : RequestBase(Messaging.RequestCode.SessionStatus);