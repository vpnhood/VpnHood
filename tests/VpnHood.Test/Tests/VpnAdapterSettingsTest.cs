using System.Runtime.CompilerServices;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Test.Tests;

[TestClass]
public class VpnAdapterSettingsTest
{
    // The platforms' settings copied the host's one by one and left some behind. The copy takes every optional setting,
    // and the compiler asks each platform for the required ones. Each optional one gets a value other than its default
    // here, so one added later and left out of the copy fails, and so does one that is required no more
    [TestMethod]
    public void A_copy_of_adapter_settings_keeps_every_optional_setting()
    {
        var settings = new VpnAdapterSettings { AdapterName = "source", Blocking = true };
        var defaults = new VpnAdapterSettings { AdapterName = "default", Blocking = false };
        var optionalProperties = typeof(VpnAdapterSettings).GetProperties()
            .Where(property => !property.IsDefined(typeof(RequiredMemberAttribute), inherit: false))
            .ToArray();

        Assert.IsNotEmpty(optionalProperties);
        foreach (var property in optionalProperties) {
            property.SetValue(settings, property.PropertyType switch {
                var type when type == typeof(string) => "changed",
                var type when type == typeof(bool) => !(bool)(property.GetValue(defaults) ?? false),
                var type when type == typeof(int?) => 7,
                var type when type == typeof(TimeSpan) || type == typeof(TimeSpan?) => TimeSpan.FromSeconds(7),
                var type => throw new InvalidOperationException(
                    $"Give the test a value of {type.Name} for {property.Name}.")
            });
        }

        var copy = new VpnAdapterSettings(settings) { AdapterName = "copy", Blocking = false };
        foreach (var property in optionalProperties)
            Assert.AreEqual(property.GetValue(settings), property.GetValue(copy), property.Name);
    }
}
