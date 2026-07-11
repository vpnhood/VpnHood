using System.Reflection;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.Utils;

public class AppConfigsBase<T> : Singleton<T> where T : Singleton<T>
{
    // "AppSettings.json" and "AppSettings_Environment.json" are embedded by the app csproj from the
    // private ".user" folder (see the EmbeddedResource items). Both are optional: a fork without them
    // still builds and runs on the in-code defaults, and the environment layer overrides the base.
    protected void LoadConfig()
    {
        Merge("AppSettings.json");
        Merge("AppSettings_Environment.json");
    }

    private void Merge(string resourceName)
    {
        var json = ReadResource(typeof(T).Assembly, resourceName);
        if (!string.IsNullOrEmpty(json))
            JsonSerializerExt.PopulateObject(this, json, typeof(T));
    }

    // Reads an embedded text resource (e.g. a secret embedded by the app csproj). Returns null when the
    // resource was not embedded, so a fork without it falls back to in-code defaults.
    protected string? ReadResourceText(string resourceName)
    {
        return ReadResource(typeof(T).Assembly, resourceName);
    }

    private static string? ReadResource(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
