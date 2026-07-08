using ObjCRuntime;
using VpnHood.Core.Client.Devices.Ios;

namespace VpnHood.App.Connect.Ios;

// .NET 11 / CoreCLR fix: under the managed-static registrar (the CoreCLR default), pointing
// NSExtensionPrincipalClass straight at the core `IosVpnService` crashes the extension on launch with
//   ObjCRuntime.RuntimeException: Could not find the assembly VpnHood.Core.Client.Devices.Ios in the loaded assemblies
// because the principal class lives in a referenced assembly the registrar never loads (the Extension
// had no code of its own rooting it). Mono tolerated this; CoreCLR does not.
//
// A thin subclass IN THE EXTENSION'S OWN ASSEMBLY that derives from core IosVpnService roots the core
// assembly (hard base-type dependency) so the registrar loads + registers it. Info.plist's
// NSExtensionPrincipalClass must match the [Register] name below ("PacketTunnelProvider").
[Register("PacketTunnelProvider")]
public class PacketTunnelProvider : IosVpnService
{
    // The .NET iOS runtime calls this when ObjC instantiates the extension's principal class.
    protected PacketTunnelProvider(NativeHandle handle) : base(handle)
    {
    }
}
