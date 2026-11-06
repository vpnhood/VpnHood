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

    // A converter only has to be made. All would also keep JsonConverter's reflection members, which the
    // trim analysis then reports wherever this call is kept.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(ArrayConverter<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(NullToEmptyArrayConverter<>))]

    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(IPAddressConverter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(IPEndPointConverter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(TimeSpanConverter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(VersionConverter))]

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IpRange))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(IpRangeConverter))]

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPNetwork))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IpNetwork))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(IpNetworkConverter))]

    // The trimmer reads the attributes, not the body: the types stay wherever this call is kept.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void PreserveTypes()
    {
    }
}
