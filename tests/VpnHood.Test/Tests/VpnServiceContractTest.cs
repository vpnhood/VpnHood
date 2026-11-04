using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Net.Toolkit.Converters;

namespace VpnHood.Test.Tests;

// The contract between an app and its VPN engine (docs/client-architecture.md): what keeps it one that
// another engine, or another app, can keep across releases.
[TestClass]
public class VpnServiceContractTest : TestBase
{
    // Every type the contract's files and messages carry, from its roots.
    private static IReadOnlyList<Type> ContractRoots { get; } = [
        typeof(VpnServiceOptions),
        typeof(ConnectionInfo),
        typeof(ApiResponse<object>),
        typeof(VpnEngineInfo),
        .. typeof(IApiRequest).Assembly.GetTypes()
            .Where(x => typeof(IApiRequest).IsAssignableFrom(x) && x is { IsInterface: false, IsAbstract: false })
            .OrderBy(x => x.Name)
    ];

    [TestMethod]
    public void Every_enum_the_contract_carries_reads_an_unknown_name_as_its_fallback()
    {
        var enumReaders = GetEnumReaders();
        Assert.IsNotEmpty(enumReaders);

        var failures = new List<string>();
        foreach (var (name, enumType, converterType) in enumReaders) {
            // a fallback converter, or one of its own that reads as the enum's JsonEnumFallback (EndPointStrategy's)
            var fallback = converterType == null ? null
                : IsFallbackConverter(converterType) ? GetFallback(converterType)
                : enumType.GetCustomAttribute<JsonEnumFallbackAttribute>()?.Value;
            if (converterType == null || fallback == null) {
                failures.Add($"{name}: no fallback");
                continue;
            }

            var options = new JsonSerializerOptions {
                Converters = { (JsonConverter)Activator.CreateInstance(converterType)! }
            };
            foreach (var json in new[] { "\"NoSuchMemberYet\"", "987654" }) {
                try {
                    var value = JsonSerializer.Deserialize(json, enumType, options);
                    if (!Equals(value, fallback))
                        failures.Add($"{name}: read {json} as {value}, not as its fallback {fallback}");
                }
                catch (JsonException ex) {
                    failures.Add($"{name}: {json} failed: {ex.Message}");
                }
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    // Each enum the contract carries, with the converter that reads it: its own, or a property's.
    private static IReadOnlyList<(string Name, Type EnumType, Type? ConverterType)> GetEnumReaders()
    {
        var types = GetContractTypes();
        var ofTypes = types
            .Where(x => x.IsEnum)
            .Select(x => (x.FullName!, x, x.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType));

        var ofProperties = types
            .Where(x => x is { IsEnum: false, IsArray: false } && x.Namespace?.StartsWith("VpnHood", StringComparison.Ordinal) == true)
            .SelectMany(x => x.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Select(x => (Property: x, EnumType: Nullable.GetUnderlyingType(x.PropertyType) ?? x.PropertyType,
                Converter: x.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType))
            .Where(x => x.EnumType.IsEnum && x.Converter != null)
            .Select(x => ($"{x.Property.DeclaringType!.Name}.{x.Property.Name}", x.EnumType, x.Converter));

        return ofTypes.Concat(ofProperties).ToArray();
    }

    private static bool IsFallbackConverter(Type converterType)
    {
        for (var type = converterType; type != null; type = type.BaseType)
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(JsonStringEnumFallbackConverter<>))
                return true;

        return false;
    }

    private static object? GetFallback(Type converterType) =>
        converterType.GetProperty("Fallback")?.GetValue(Activator.CreateInstance(converterType));

    [TestMethod]
    public void A_dictionary_keyed_by_an_enum_reads_an_unknown_key_as_its_fallback()
    {
        var localization = JsonSerializer.Deserialize<VpnServiceLocalization>(
            """{ "ClientStates": { "Connected": "Connected", "NoSuchStateYet": "New" } }""");

        Assert.IsNotNull(localization);
        Assert.AreEqual("Connected", localization.ClientStates[Core.Client.Abstractions.ClientState.Connected]);
    }

    [TestMethod]
    public void The_app_writes_the_highest_version_both_sides_speak()
    {
        Assert.AreEqual(VpnServiceContract.Version, VpnServiceContract.Negotiate(VpnEngineInfo.VpnHoodEngine));
        Assert.AreEqual(VpnServiceContract.Version, VpnServiceContract.Negotiate(CreateEngine(1, 99)));
    }

    [TestMethod]
    public void An_engine_that_shares_no_version_fails_the_connect()
    {
        var ex = Assert.ThrowsExactly<VpnServiceContractException>(() =>
            VpnServiceContract.Negotiate(CreateEngine(VpnServiceContract.Version + 1, VpnServiceContract.Version + 2)));
        StringAssert.Contains(ex.Message, "Test engine");
    }

    [TestMethod]
    public void An_engine_refuses_options_of_a_version_it_does_not_speak()
    {
        VpnServiceContract.Verify(VpnServiceContract.Version);
        Assert.ThrowsExactly<VpnServiceContractException>(() => VpnServiceContract.Verify(VpnServiceContract.Version + 1));
        Assert.ThrowsExactly<VpnServiceContractException>(() => VpnServiceContract.Verify(0));
    }

    [TestMethod]
    public void Options_from_before_versions_read_as_the_first()
    {
        var options = JsonSerializer.Deserialize<VpnServiceOptions>(
            """{ "ClientOptions": { "AdapterName": "a", "AccessKey": "k", "ClientId": "c" } }""");

        Assert.IsNotNull(options);
        Assert.AreEqual(1, options.ContractVersion);
    }

    [TestMethod]
    public void This_library_s_engine_has_every_feature()
    {
        // a feature added to the contract is one this library's engine has
        var features = VpnEngineInfo.VpnHoodEngine.Features;
        foreach (var property in typeof(VpnEngineFeatures).GetProperties().Where(x => x.PropertyType == typeof(bool)))
            Assert.IsTrue((bool)property.GetValue(features)!, property.Name);
        CollectionAssert.AreEquivalent(new[] { TrackerKinds.Ga4, TrackerKinds.DotNet }, features.TrackerKinds.ToArray());
        Assert.AreEqual(VpnServiceContract.MinVersion, VpnEngineInfo.VpnHoodEngine.MinContractVersion);
        Assert.AreEqual(VpnServiceContract.Version, VpnEngineInfo.VpnHoodEngine.MaxContractVersion);
    }

    [TestMethod]
    public void A_report_reads_a_feature_it_leaves_out_as_lacking_and_skips_one_it_does_not_know()
    {
        var vpnEngineInfo = JsonSerializer.Deserialize<VpnEngineInfo>("""
            { "Name": "Test engine", "Version": "1.0.0", "MinContractVersion": 1, "MaxContractVersion": 1,
              "Features": { "KillSwitch": true, "NoSuchFeatureYet": true } }
            """);

        Assert.IsNotNull(vpnEngineInfo);
        Assert.IsTrue(vpnEngineInfo.Features.KillSwitch);
        Assert.IsFalse(vpnEngineInfo.Features.Proxies);
        Assert.IsEmpty(vpnEngineInfo.Features.TrackerKinds);
    }

    private static VpnEngineInfo CreateEngine(int minVersion, int maxVersion) => new() {
        Name = "Test engine",
        Version = "1.0.0",
        MinContractVersion = minVersion,
        MaxContractVersion = maxVersion,
        Features = new VpnEngineFeatures()
    };

    // The types reachable from the roots through what JSON writes: public properties, the elements of a list,
    // the keys and values of a dictionary. A type with a converter of its own is written as that converter
    // says, so the walk stops there.
    private static IReadOnlyList<Type> GetContractTypes()
    {
        var found = new HashSet<Type>();
        var queue = new Queue<Type>(ContractRoots);
        while (queue.TryDequeue(out var type)) {
            type = Nullable.GetUnderlyingType(type) ?? type;
            if (!found.Add(type))
                continue;

            if (type.IsArray) {
                queue.Enqueue(type.GetElementType()!);
                continue;
            }

            if (type.IsGenericType && type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true) {
                foreach (var argument in type.GetGenericArguments())
                    queue.Enqueue(argument);
                continue;
            }

            if (type.IsEnum || type.Namespace?.StartsWith("VpnHood", StringComparison.Ordinal) != true ||
                type.GetCustomAttribute<JsonConverterAttribute>() != null)
                continue;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() is { Condition: JsonIgnoreCondition.Always } ||
                    property.GetCustomAttribute<JsonConverterAttribute>() != null ||
                    property.GetIndexParameters().Length > 0)
                    continue;

                queue.Enqueue(property.PropertyType);
            }
        }

        return found.ToArray();
    }
}
