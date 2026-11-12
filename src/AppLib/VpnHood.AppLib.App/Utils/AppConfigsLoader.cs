using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using VpnHood.AppLib.Api.App;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLib.App.Utils;

/// <summary>
/// Provides a product's settings from the settings files its project embeds from the private <c>.user</c> folder.
/// </summary>
public static class AppConfigsLoader
{
    /// <summary>
    /// Creates a product's settings and fills them from the embedded <c>AppSettings.json</c>, then
    /// <c>AppSettings_Environment.json</c>, the build configuration's: each overrides by name and skips a key the type
    /// does not have.
    /// </summary>
    /// <remarks>
    /// Both files are optional: a fork without them builds, and its settings keep their defaults. The type's public
    /// properties, and those of <see cref="AppLinks"/>, are kept for the trimmer, as only reflection sets them.
    /// </remarks>
    /// <typeparam name="T">The product's settings type, in the assembly that embeds the files.</typeparam>
    /// <returns>The settings.</returns>
    /// <exception cref="System.Text.Json.JsonException">
    /// A file is not JSON, or a value does not fit its property.
    /// </exception>
    /// <exception cref="InvalidOperationException">A JSON value is not of the kind its property needs.</exception>
    /// <exception cref="UriFormatException">A <see cref="Uri"/> property's text is not a URI.</exception>
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicProperties, typeof(AppLinks))]
    public static T Load<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] T>()
        where T : AppConfigs, new()
    {
        var appConfigs = new T();
        Merge(appConfigs, "AppSettings.json");
        Merge(appConfigs, "AppSettings_Environment.json");
        return appConfigs;
    }

    /// <summary>Reads a text file a project embedded, such as a secret.</summary>
    /// <param name="assembly">The assembly that embeds it.</param>
    /// <param name="resourceName">The embedded resource's name.</param>
    /// <returns>The file's text, or <see langword="null"/> when this build has no such file.</returns>
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
