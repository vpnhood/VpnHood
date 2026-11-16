using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using Windows.Foundation;
using WinRT;

namespace VpnHood.Core.Client.VpnServices.Host.WindowsVpnPlugin;

/// <summary>
/// Provides the activation factories the VPN host hands Windows (<c>CoreApplication.RunWithActivationFactories</c>).
/// </summary>
public sealed class ActivationFactoryProvider : IGetActivationFactory
{
    /// <inheritdoc/>
    /// <remarks>It gives the factories of the classes this component authors.</remarks>
    /// <exception cref="TypeLoadException">The component has no such class.</exception>
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
