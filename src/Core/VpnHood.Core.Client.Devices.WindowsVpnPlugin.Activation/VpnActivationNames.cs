namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin.Activation;

public static class VpnActivationNames
{
    // The class C#/WinRT generates the IActivatableApplication factory under: the name Windows derived for the
    // verified package, from its application id (VpnPluginHost) and a hash of its identity. Another identity's
    // name gets the same factory (ActivationFactoryProvider). This and the mapping in
    // WinRT.Host.runtimeconfig.json change together.
    public const string GeneratedApplicationClass = "VpnPluginHost.AppXc6d7gqsjtff8fc4mjx68xsve09xhd10s.mca";
}
