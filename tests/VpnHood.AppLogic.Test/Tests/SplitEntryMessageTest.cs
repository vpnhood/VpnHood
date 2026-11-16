using System.Globalization;
using System.Text;
using VpnHood.AppLogic.Api.Exceptions;
using VpnHood.AppUi.Common;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Assets;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.AppLogic.Test.Tests;

// A split list's refusal is said in the person's language, with the entry it names: the app's error carries the entry
// and why, where its own words, in English, were shown as they were.
[TestClass]
[DoNotParallelize] // the language is the process's
public class SplitEntryMessageTest
{
    private sealed class WordsStore : IAssetProvider
    {
        private readonly Dictionary<string, string> _files = new() {
            ["locales/en.json"] = """
                { "SPLIT_DOMAIN_ASTERISK_ERROR": "No asterisk: {entry}", "SPLIT_IP_INVALID_ERROR": "No IP: {entry}" }
                """,
            ["locales/fa.json"] = """
                { "SPLIT_DOMAIN_ASTERISK_ERROR": "بدون ستاره: {entry}", "SPLIT_IP_INVALID_ERROR": "آی‌پی نیست: {entry}" }
                """,
            ["locales/index.json"] = """["en", "fa"]"""
        };

        public Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
        {
            return _files.TryGetValue(assetPath, out var text)
                ? Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(text)))
                : throw new AssetNotFoundException(assetPath);
        }
    }

    [TestCleanup]
    public async Task BackToEnglish()
    {
        await Strings.Current.SetCultureAsync(CultureInfo.GetCultureInfo("en"), CancellationToken.None);
    }

    [TestMethod]
    public async Task A_refused_entry_is_said_in_the_persons_language()
    {
        await Strings.InitAsync(new WordsStore(), CancellationToken.None);
        await Strings.Current.SetCultureAsync(CultureInfo.GetCultureInfo("fa"), CancellationToken.None);

        // in process, and as an error that came over HTTP, which carries the data as text
        var asterisk = SplitEntryException.Create(SplitEntryError.Asterisk, "*ads.example.com");
        Assert.AreEqual("بدون ستاره: *ads.example.com", DialogText(ErrorMessages.For(asterisk, new ErrorContext())));
        Assert.AreEqual("بدون ستاره: *ads.example.com",
            DialogText(ErrorMessages.For(JsonUtils.JsonClone(asterisk.ToApiError()), new ErrorContext())));

        var ipRange = SplitEntryException.Create(SplitEntryError.InvalidIpRange, "10.0.0.300");
        Assert.AreEqual("آی‌پی نیست: 10.0.0.300", DialogText(ErrorMessages.For(ipRange, new ErrorContext())));
    }

    private static string DialogText(ErrorMessage message)
    {
        Assert.IsInstanceOfType<ErrorMessage.Dialog>(message);
        return ((ErrorMessage.Dialog)message).Text;
    }
}
