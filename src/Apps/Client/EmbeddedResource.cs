using System.Reflection;

namespace VpnHood.App.Client;

internal static class EmbeddedResource
{
    // A resource a build may or may not embed: its bytes, or null when this build has none.
    public static byte[]? TryRead(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        using var memoryStream = new MemoryStream();
        stream.CopyTo(memoryStream);
        return memoryStream.ToArray();
    }
}
