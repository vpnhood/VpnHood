using System.Globalization;
using System.Text;
using VpnHood.AppUi.Common;
using VpnHood.Net.Toolkit.Assets;

namespace VpnHood.AppLib.Test.Tests;

[TestClass]
[DoNotParallelize] // the language is the process's
public class CountryNamesTest
{
    private sealed class CountriesStore(params (string Path, string Text)[] countries) : IAssetProvider
    {
        private readonly Dictionary<string, string> _files = new[] {
            ("locales/en.json", "{}"),
            ("locales/index.json", """["en"]""")
        }.Concat(countries).ToDictionary(x => x.Item1, x => x.Item2);

        public Task<Stream> OpenReadAsync(string assetPath, CancellationToken cancellationToken)
        {
            return _files.TryGetValue(assetPath, out var text)
                ? Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(text)))
                : throw new AssetNotFoundException(assetPath);
        }
    }

    // a language already set reads nothing, and it may be one set over another store
    private static async Task ShowLanguage(IAssetProvider store, string cultureName)
    {
        await Strings.InitAsync(store, CancellationToken.None);
        await Strings.Current.SetCultureAsync(CultureInfo.GetCultureInfo("en"), CancellationToken.None);
        await Strings.Current.SetCultureAsync(CultureInfo.GetCultureInfo(cultureName), CancellationToken.None);
    }

    [TestCleanup]
    public async Task BackToEnglish()
    {
        await Strings.Current.SetCultureAsync(CultureInfo.GetCultureInfo("en"), CancellationToken.None);
    }

    [TestMethod]
    public async Task A_name_goes_by_the_language_then_English_then_the_caller_then_the_code()
    {
        await ShowLanguage(new CountriesStore(
            ("countries/en.json", """{ "DE": "Germany", "XK": "Kosovo" }"""),
            ("countries/zh.json", """{ "DE": "德国" }""")), "zh");

        Assert.AreEqual("德国", CountryNames.Get("DE"));
        Assert.AreEqual("德国", CountryNames.Get("de"));
        Assert.AreEqual("德国", CountryNames.Get("DE", "A server's name"));
        Assert.AreEqual("Kosovo", CountryNames.Get("XK", "A server's name"));
        Assert.AreEqual("A server's name", CountryNames.Get("QQ", "A server's name"));
        Assert.AreEqual("QQ", CountryNames.Get("QQ"));
    }

    [TestMethod]
    public async Task A_culture_with_no_file_takes_its_language_and_a_language_with_none_takes_English()
    {
        var store = new CountriesStore(
            ("countries/en.json", """{ "DE": "Germany" }"""),
            ("countries/pt.json", """{ "DE": "Alemanha" }"""));

        await ShowLanguage(store, "pt-BR");
        Assert.AreEqual("Alemanha", CountryNames.Get("DE"));

        await ShowLanguage(store, "vi");
        Assert.AreEqual("Germany", CountryNames.Get("DE"));
    }

    [TestMethod]
    public async Task A_file_for_the_culture_goes_before_its_language()
    {
        await ShowLanguage(new CountriesStore(
            ("countries/pt.json", """{ "DE": "Alemanha" }"""),
            ("countries/pt-BR.json", """{ "DE": "Alemanha do Brasil" }""")), "pt-BR");

        Assert.AreEqual("Alemanha do Brasil", CountryNames.Get("DE"));
    }

    // Android prepares the UI for every activity, and nothing asks for the language again
    [TestMethod]
    public async Task A_language_keeps_its_names_when_a_new_activity_prepares_the_words()
    {
        var store = new CountriesStore(
            ("countries/en.json", """{ "DE": "Germany" }"""),
            ("countries/zh.json", """{ "DE": "德国" }"""));

        await ShowLanguage(store, "zh");
        await Strings.InitAsync(store, CancellationToken.None);

        Assert.AreEqual("德国", CountryNames.Get("DE"));
    }

    [TestMethod]
    public async Task A_store_with_no_names_leaves_the_caller_and_the_code()
    {
        await ShowLanguage(new CountriesStore(), "zh");

        Assert.AreEqual("A server's name", CountryNames.Get("DE", "A server's name"));
        Assert.AreEqual("DE", CountryNames.Get("DE"));
    }
}
