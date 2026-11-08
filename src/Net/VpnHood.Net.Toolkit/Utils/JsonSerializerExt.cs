using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Provides the means to merge JSON into an existing object, by reflection.
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class JsonSerializerExt
{
    /// <summary>
    /// Sets an object's properties from the JSON's, by exact name: a value-type, string or Uri property, a JSON array or a
    /// null replaces the value; another JSON object merges into the property's current object, and a null property stays
    /// null. A name the type does not have is passed over.
    /// </summary>
    /// <typeparam name="T">The object's type, whose properties are set.</typeparam>
    /// <param name="target">The object.</param>
    /// <param name="jsonSource">The JSON object.</param>
    /// <exception cref="JsonException">The text is not JSON, or a value does not fit its property.</exception>
    /// <exception cref="InvalidOperationException">A JSON value is not of the kind the property needs.</exception>
    /// <exception cref="UriFormatException">A Uri property's text is not a URI.</exception>
    public static void PopulateObject<T>(T target, string jsonSource) where T : class
    {
        PopulateObject(target, jsonSource, typeof(T));
    }

    /// <summary>Sets an object's properties from the JSON's, as <see cref="PopulateObject{T}(T, string)"/> does.</summary>
    /// <param name="target">The object.</param>
    /// <param name="jsonSource">The JSON object.</param>
    /// <param name="type">The type whose properties are set.</param>
    /// <exception cref="JsonException">The text is not JSON, or a value does not fit its property.</exception>
    /// <exception cref="InvalidOperationException">A JSON value is not of the kind the property needs.</exception>
    /// <exception cref="UriFormatException">A Uri property's text is not a URI.</exception>
    public static void PopulateObject(object target, string jsonSource, Type type)
    {
        var json = JsonDocument.Parse(jsonSource).RootElement;

        foreach (var property in json.EnumerateObject()) {
            OverwriteProperty(target, property, type);
        }
    }

    private static void OverwriteProperty(object target, JsonProperty updatedProperty, Type type)
    {
        var propertyInfo = type.GetProperty(updatedProperty.Name);

        if (propertyInfo == null) {
            return;
        }

        var propertyType = propertyInfo.PropertyType;
        object? parsedValue;

        if (propertyType == typeof(Uri)) {
            parsedValue = updatedProperty.Value.GetString() is { } uriString
                ? new Uri(uriString, UriKind.RelativeOrAbsolute)
                : null;
        }
        else if (propertyType.IsValueType || propertyType == typeof(string) ||
                 updatedProperty.Value.ValueKind is JsonValueKind.Array or JsonValueKind.Null) {
            // arrays (e.g. string[] product ids) and explicit nulls replace the value wholesale;
            // only JSON objects merge property-by-property below
            parsedValue = JsonSerializer.Deserialize(updatedProperty.Value.GetRawText(), propertyType);
        }
        else {
            parsedValue = propertyInfo.GetValue(target);
            if (parsedValue != null)
                PopulateObject(
                    parsedValue,
                    updatedProperty.Value.GetRawText(),
                    propertyType);
        }

        propertyInfo.SetValue(target, parsedValue);
    }
}