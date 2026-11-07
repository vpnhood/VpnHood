using System.Globalization;
using System.Text;
using VpnHood.AppUi.Common;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
public class StringsTest
{
    private sealed class WordsStore : IAssetProvider
    {
        private readonly Dictionary<string, string> _files = new() {
            ["locales/en.json"] = """{ "CONNECT": "Connect" }""",
            ["locales/zh.json"] = """{ "CONNECT": "连接" }""",
            ["locales/index.json"] = """["en", "zh"]"""
        };

        public Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
        {
            return _files.TryGetValue(assetPath, out var text)
                ? Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(text)))
                : throw new AssetNotFoundException(assetPath);
        }
    }

    // Android prepares the UI for every activity, and leaving by Back then reopening is a new activity in
    // the same process: nothing asks for the language again, so it must not be lost.
    [TestMethod]
    public async Task A_language_stays_when_a_new_activity_prepares_the_words()
    {
        var store = new WordsStore();
        await Strings.InitAsync(store, CancellationToken.None);
        await Strings.Current.SetCultureAsync(CultureInfo.GetCultureInfo("zh"), CancellationToken.None);
        Assert.AreEqual("连接", Strings.Current.Texts["CONNECT"]);

        await Strings.InitAsync(store, CancellationToken.None);

        Assert.AreEqual("zh", Strings.Current.CultureName);
        Assert.AreEqual("连接", Strings.Current.Texts["CONNECT"]);
    }
}
