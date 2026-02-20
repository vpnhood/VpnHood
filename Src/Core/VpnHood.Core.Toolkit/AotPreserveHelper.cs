using System.Diagnostics.CodeAnalysis;
using System.Net;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.Core.Toolkit;

public static class AotPreserveHelper
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPEndPoint))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPAddress))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Version))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TimeSpan))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ArrayConverter<,>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPAddressConverter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPEndPointConverter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(NullToEmptyArrayConverter<>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TimeSpanConverter))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(VersionConverter))]
    public static void PreserveTypes()
    {
    }
}