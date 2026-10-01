using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Toolkit;

public static class AotPreserveHelper
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPEndPoint))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPAddress))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Version))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TimeSpan))]

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArrayConverter<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NullToEmptyArrayConverter<>))]

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPAddressConverter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPEndPointConverter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TimeSpanConverter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VersionConverter))]

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IpRange))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IpRangeConverter))]

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPNetwork))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IpNetwork))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IpNetworkConverter))]

    // The trimmer reads the attributes, not the body: the types stay wherever this call is kept.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void PreserveTypes()
    {
    }
}
