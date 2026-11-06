using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class JsonUtilsTest : TestBase
{
    // ReSharper disable once ClassNeverInstantiated.Local
    private record Item(string Name);

    private sealed class Tags
    {
        [JsonConverter(typeof(NullToEmptyArrayConverter<string>))]
        public string[] Items { get; init; } = ["default"];
    }

    [TestMethod]
    public void A_null_array_reads_as_empty_and_an_array_as_it_is()
    {
        Assert.IsEmpty(JsonSerializer.Deserialize<Tags>("""{"Items":null}""")?.Items ?? ["missing"]);

        const string json = """{"Items":["a","b"]}""";
        var tags = JsonSerializer.Deserialize<Tags>(json);
        CollectionAssert.AreEqual(new[] { "a", "b" }, tags?.Items);
        Assert.AreEqual(json, JsonSerializer.Serialize(tags));
    }

    [TestMethod]
    public async Task A_json_file_reads_as_its_object_and_one_that_is_not_json_as_null()
    {
        var folderPath = Path.Combine(TestHelper.WorkingPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(folderPath);
        var goodFilePath = Path.Combine(folderPath, "good.json");
        var badFilePath = Path.Combine(folderPath, "bad.json");
        await File.WriteAllTextAsync(goodFilePath, """{ "Name": "x" }""", TestCt);
        await File.WriteAllTextAsync(badFilePath, "not json", TestCt);

        Assert.AreEqual("x", (await JsonUtils.TryDeserializeFileAsync<Item>(goodFilePath, TestCt))?.Name);
        Assert.IsNull(await JsonUtils.TryDeserializeFileAsync<Item>(badFilePath, TestCt));
    }
}
