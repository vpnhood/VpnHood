using System.Text.Json;
using VpnHood.AppLogic.Api;
using VpnHood.AppLogic.Api.Settings;

namespace VpnHood.AppLogic.Test.Tests;

// The app's API writes its enums as names, so a UI compares names and a generated client gets named enums. One keeps
// its numbers on purpose: EndPointStrategy, whose numbers are what settings.json stores.
[TestClass]
public class ApiEnumJsonTest
{
    [TestMethod]
    public void Every_enum_of_the_api_is_a_name_in_json()
    {
        var enumTypes = typeof(IAppApi).Assembly.GetExportedTypes()
            .Where(x => x.IsEnum && x != typeof(EndPointStrategy))
            .ToArray();
        var failures = new List<string>();

        foreach (var enumType in enumTypes) {
            var value = Enum.GetValues(enumType).GetValue(0);
            var json = JsonSerializer.Serialize(value, enumType);
            if (!json.StartsWith('"'))
                failures.Add($"{enumType.FullName} is written as {json}.");
        }

        Assert.IsTrue(enumTypes.Length > 20, $"Only {enumTypes.Length} enums were found in the API.");
        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void EndPointStrategy_keeps_its_numbers()
    {
        Assert.AreEqual("2", JsonSerializer.Serialize(EndPointStrategy.IpFirst));
        Assert.AreEqual(EndPointStrategy.IpFirst, JsonSerializer.Deserialize<EndPointStrategy>("\"IpFirst\""));
    }
}
