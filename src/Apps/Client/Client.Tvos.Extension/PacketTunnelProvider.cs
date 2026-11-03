using ObjCRuntime;
using VpnHood.Core.Client.VpnServices.Host.Ios;

namespace VpnHood.App.Client.Tvos.Extension;

// The extension's principal class (Info.plist's NSExtensionPrincipalClass), in its own assembly so the
// registrar loads the core one, as on iOS (Client.Ios.Extension's PacketTunnelProvider says why).
[Register("PacketTunnelProvider")]
public class PacketTunnelProvider : IosVpnService
{
    // The runtime calls this when ObjC instantiates the extension's principal class.
    protected PacketTunnelProvider(NativeHandle handle) : base(handle)
    {
    }
}
