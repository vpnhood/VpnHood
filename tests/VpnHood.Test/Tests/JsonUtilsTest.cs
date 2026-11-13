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
    public void A_redacted_value_is_hidden_whole_at_any_depth()
    {
        const string json = """
            {
              "Name" : "server",
              "Secret" : "a,b}c\"d",
              "Keys": [["k1", "k2"], ["k3"]],
              "Nested": { "Secret": { "Inner": "deep" }, "Other": [1, 2] },
              "Rate": 1.50
            }
            """;

        var redacted = JsonUtils.RedactValue(json, ["Secret", "Keys"]);
        foreach (var hidden in new[] { "a,b", "c\\\"d", "k1", "k2", "k3", "deep" })
            Assert.IsFalse(redacted.Contains(hidden), $"'{hidden}' is still in: {redacted}");

        using var document = JsonDocument.Parse(redacted);
        var root = document.RootElement;
        Assert.AreEqual("server", root.GetProperty("Name").GetString());
        Assert.AreEqual("***", root.GetProperty("Secret").GetString());
        Assert.AreEqual("***", root.GetProperty("Keys").EnumerateArray().Single().GetString());
        Assert.AreEqual("***", root.GetProperty("Nested").GetProperty("Secret").GetString());
        Assert.AreEqual(2, root.GetProperty("Nested").GetProperty("Other").GetArrayLength());
        Assert.AreEqual("1.50", root.GetProperty("Rate").GetRawText());

        // written as it came: on several lines, or on one
        Assert.IsTrue(redacted.Contains('\n'));
        Assert.AreEqual("""{"Secret":"***","Id":3}""", JsonUtils.RedactValue("""{"Secret":null,"Id":3}""", ["Secret"]));
    }

    [TestMethod]
    public void Text_that_is_no_json_is_not_given_back_unredacted()
    {
        Assert.Throws<JsonException>(() => JsonUtils.RedactValue("""{"Secret": "abc", """, ["Secret"]));
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
