using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class ServerCheckRequest()
    : ClientRequest((byte)Messaging.RequestCode.ServerCheck);
