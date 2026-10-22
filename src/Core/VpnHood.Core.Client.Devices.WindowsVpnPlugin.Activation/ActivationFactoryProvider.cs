using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using Windows.Foundation;
using WinRT;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation;

// The factories the VPN host hands Windows (CoreApplication.RunWithActivationFactories).
public sealed class ActivationFactoryProvider : IGetActivationFactory
{
    public object GetActivationFactory(string activatableClassId)
    {
        VhLogger.Instance.LogInformation("Windows asked for the activation factory of {ClassId}.", activatableClassId);
        var factory = global::WinRT.Module.GetActivationFactory(activatableClassId);

        // The .mca name Windows asks for carries a hash of the package's identity; the generated application
        // factory serves any identity, so a fork's package activates too.
        if (factory == IntPtr.Zero && activatableClassId.EndsWith(".mca", StringComparison.OrdinalIgnoreCase))
            factory = global::WinRT.Module.GetActivationFactory(VpnActivationNames.GeneratedApplicationClass);

        if (factory == IntPtr.Zero)
            throw new TypeLoadException("No activation factory exists for " + activatableClassId);

        try {
            return MarshalInspectable<object>.FromAbi(factory);
        }
        finally {
            MarshalInspectable<object>.DisposeAbi(factory);
        }
    }
}
