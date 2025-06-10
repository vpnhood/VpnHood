using System.Diagnostics.CodeAnalysis;
using System.Net;

namespace VpnHood.Core.Toolkit;

public static class AotPreserveHelper
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPEndPoint))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IPAddress))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Version))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(TimeSpan))]
    public static void PreserveTypes() { }
}