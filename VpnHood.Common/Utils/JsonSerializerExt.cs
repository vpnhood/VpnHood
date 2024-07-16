using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace VpnHood.Common.Utils;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public static class JsonSerializerExt
{
    // Dynamically attach a JsonSerializerOptions copy that is configured using PopulateTypeInfoResolver
    private static readonly ConditionalWeakTable<JsonSerializerOptions, JsonSerializerOptions> SPopulateMap = new();

    public static void PopulateObject(string json, Type returnType, object destination,
        JsonSerializerOptions? options = null)
    {
        options = GetOptionsWithPopulateResolver(options);
        PopulateTypeInfoResolver.TargetPopulateObject = destination;
        try {
            var result = JsonSerializer.Deserialize(json, returnType, options);
            Debug.Assert(ReferenceEquals(result, destination));
        }
        finally {
            PopulateTypeInfoResolver.TargetPopulateObject = null;
        }
    }

    private static JsonSerializerOptions GetOptionsWithPopulateResolver(JsonSerializerOptions? options)
    {
        options ??= JsonSerializerOptions.Default;

        if (!SPopulateMap.TryGetValue(options, out var populateResolverOptions)) {
            JsonSerializer.Serialize(value: 0, options); // Force a serialization to mark options as read-only
            Debug.Assert(options.TypeInfoResolver != null);

            populateResolverOptions = new JsonSerializerOptions(options) {
                TypeInfoResolver = new PopulateTypeInfoResolver(options.TypeInfoResolver)
            };

            SPopulateMap.Add(options, populateResolverOptions);
        }

        Debug.Assert(options.TypeInfoResolver is PopulateTypeInfoResolver);
        return populateResolverOptions;
    }

    private class PopulateTypeInfoResolver(IJsonTypeInfoResolver jsonTypeInfoResolver) : IJsonTypeInfoResolver
    {
        [ThreadStatic] internal static object? TargetPopulateObject;

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            var typeInfo = jsonTypeInfoResolver.GetTypeInfo(type, options);
            if (typeInfo == null || typeInfo.Kind == JsonTypeInfoKind.None)
                return typeInfo;

            var defaultCreateObjectDelegate = typeInfo.CreateObject;
            typeInfo.CreateObject = () => {
                var result = TargetPopulateObject;
                if (result != null) {
                    // clean up to prevent reuse in recursive scenario
                    TargetPopulateObject = null;
                }
                else {
                    // fall back to the default delegate
                    result = defaultCreateObjectDelegate?.Invoke();
                }

                return result!;
            };

            return typeInfo;
        }
    }
}