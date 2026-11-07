using System.Globalization;
using VpnHood.AppLib.Abstractions;
using VpnHood.AppLib.Abstractions.UiContexts;
using VpnHood.AppLib.App.Utils;

namespace VpnHood.AppLib.Test.Tests;

// The app's language where the OS keeps it too, as Android's and iOS's settings do: a choice made on
// either side reaches the other.
[TestClass]
[DoNotParallelize] // a resume reaches every app in the process, and these read the language it takes
public class AppCultureTest : TestAppBase
{
    private sealed class OsCultureProvider : IAppCultureProvider
    {
        public string[] SystemCultures { get; set; } = ["en-US"];
        public IReadOnlyList<string> AvailableCultures { get; set; } = ["de", "en", "pt", "pt-BR", "zh"];
        public string[] SelectedCultures { get; set; } = [];
    }

    [TestMethod]
    public async Task A_language_chosen_in_the_OS_survives_the_app_starting()
    {
        var provider = new OsCultureProvider { SelectedCultures = ["de"] };
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.CultureProvider = provider;

        await using var app = TestAppHelper.CreateClientApp(appOptions);

        CollectionAssert.AreEqual(new[] { "de" }, provider.SelectedCultures);
        Assert.AreEqual("de", app.UserSettings.CultureCode);
    }

    [TestMethod]
    public async Task A_language_chosen_in_the_OS_while_the_app_runs_comes_on_resume()
    {
        var provider = new OsCultureProvider();
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.CultureProvider = provider;
        await using var app = TestAppHelper.CreateClientApp(appOptions);

        provider.SelectedCultures = ["de"];
        AppUiContext.NotifyResumed();
        Assert.AreEqual("de", app.UserSettings.CultureCode);

        // and saving any other setting no longer writes the old language over it
        await app.Api.App.SetUserSettings(app.UserSettings, TestCt);
        CollectionAssert.AreEqual(new[] { "de" }, provider.SelectedCultures);
    }

    // Android makes a new activity for it, whose UI configures the app before the resume comes.
    [TestMethod]
    public async Task A_language_chosen_in_the_OS_survives_settings_applying_with_nothing_changed()
    {
        var provider = new OsCultureProvider { SelectedCultures = ["fr"], AvailableCultures = ["de", "en", "fr"] };
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.CultureProvider = provider;
        await using var app = TestAppHelper.CreateClientApp(appOptions);

        provider.SelectedCultures = ["de"];
        app.UpdateUi();

        CollectionAssert.AreEqual(new[] { "de" }, provider.SelectedCultures);
        Assert.AreEqual("de", app.UserSettings.CultureCode);
    }

    [TestMethod]
    public async Task System_default_chosen_in_the_OS_clears_the_language()
    {
        var provider = new OsCultureProvider { SelectedCultures = ["de"] };
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.CultureProvider = provider;
        await using var app = TestAppHelper.CreateClientApp(appOptions);

        provider.SelectedCultures = [];
        AppUiContext.NotifyResumed();

        Assert.IsNull(app.UserSettings.CultureCode);
    }

    [TestMethod]
    public async Task A_language_chosen_in_the_app_reaches_the_OS()
    {
        var provider = new OsCultureProvider();
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.CultureProvider = provider;
        await using var app = TestAppHelper.CreateClientApp(appOptions);

        app.UserSettings.CultureCode = "zh";
        await app.Api.App.SetUserSettings(app.UserSettings, TestCt);
        CollectionAssert.AreEqual(new[] { "zh" }, provider.SelectedCultures);

        app.UserSettings.CultureCode = null;
        await app.Api.App.SetUserSettings(app.UserSettings, TestCt);
        Assert.IsEmpty(provider.SelectedCultures);
    }

    [TestMethod]
    public async Task A_language_as_the_OS_names_it_becomes_the_apps()
    {
        var provider = new OsCultureProvider { SelectedCultures = ["zh-Hans-US"] };
        var appOptions = TestAppHelper.CreateAppOptions();
        appOptions.CultureProvider = provider;

        await using var app = TestAppHelper.CreateClientApp(appOptions);

        Assert.AreEqual("zh", app.UserSettings.CultureCode);
    }

    [TestMethod]
    public void The_nearest_language_is_the_one_with_words()
    {
        IAppCultureProvider provider = new OsCultureProvider();

        Assert.AreEqual("zh", provider.ToAvailableCulture(new CultureInfo("zh-Hans-US")).Name);
        Assert.AreEqual("de", provider.ToAvailableCulture(new CultureInfo("de-US")).Name);
        Assert.AreEqual("pt-BR", provider.ToAvailableCulture(new CultureInfo("pt-BR")).Name);
        Assert.AreEqual("pt", provider.ToAvailableCulture(new CultureInfo("pt-PT")).Name);
        Assert.AreEqual("ja-JP", provider.ToAvailableCulture(new CultureInfo("ja-JP")).Name);
    }
}
