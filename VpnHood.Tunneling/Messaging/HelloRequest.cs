using VpnHood.Common.Messaging;

namespace VpnHood.Tunneling.Messaging;

public class HelloRequest() : SessionRequest((byte)Messaging.RequestCode.Hello);
