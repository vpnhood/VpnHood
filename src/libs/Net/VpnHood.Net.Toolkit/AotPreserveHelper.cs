using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Toolkit;

/// <summary>
/// Provides the means to ask the trimmer of a trimmed or AOT-compiled app to keep the types VpnHood's JSON reaches by
/// reflection: the network types and their converters.
/// </summary>
public static class AotPreserveHelper
{
    /// <summary>
    /// Keeps the types wherever a call to it is kept: the trimmer reads the method's attributes, and the call itself
    /// does nothing.
    /// </summary>
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
