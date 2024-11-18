using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace VpnHood.Common.Utils;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class JsonSerializerExt
{
    public static void PopulateObject<T>(T target, string jsonSource) where T : class
    {
        PopulateObject(target, jsonSource, typeof(T));
    }

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

        if (propertyType.IsValueType || propertyType == typeof(string)) {
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