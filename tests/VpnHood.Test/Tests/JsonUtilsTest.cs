using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Test.Tests;

[TestClass]
public class JsonUtilsTest : TestBase
{
    // ReSharper disable once ClassNeverInstantiated.Local
    private record Item(string Name);

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
