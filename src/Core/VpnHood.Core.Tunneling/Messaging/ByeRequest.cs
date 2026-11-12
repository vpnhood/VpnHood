namespace VpnHood.Core.Tunneling.Messaging;

/// <summary>Represents a session's end: the server answers it, then closes the session.</summary>
public class ByeRequest() : RequestBase(Messaging.RequestCode.Bye);