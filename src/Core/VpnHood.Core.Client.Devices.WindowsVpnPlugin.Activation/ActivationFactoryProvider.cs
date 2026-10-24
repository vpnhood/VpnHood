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
