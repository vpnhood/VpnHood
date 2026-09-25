using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Utils;

// Fills a product's settings from the appsettings its project embeds from the private ".user"
// folder: "AppSettings.json", then "AppSettings_Environment.json" (the build configuration's), each
// overriding by name and skipping a key the type does not have. Both are optional: a fork without
// them builds, and its settings stay unset. The type's properties are kept for the trimmer, since
// nothing but reflection sets them.
public static class AppConfigsLoader
{
    public static T Load<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>()
        where T : AppConfigs, new()
    {
        var appConfigs = new T();
        Merge(appConfigs, "AppSettings.json");
        Merge(appConfigs, "AppSettings_Environment.json");
        return appConfigs;
    }

    // A text file a project embedded - a secret, say - or null when this build has none.
    public static string? ReadResourceText(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
            return null;

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void Merge<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>(
        T appConfigs, string resourceName) where T : AppConfigs
    {
        var json = ReadResourceText(typeof(T).Assembly, resourceName);
        if (!string.IsNullOrEmpty(json))
            JsonSerializerExt.PopulateObject(appConfigs, json, typeof(T));
    }
}
