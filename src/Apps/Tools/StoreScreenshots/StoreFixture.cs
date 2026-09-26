using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.Device;

namespace VpnHood.App.StoreScreenshots;

// The web UI engine's fixture.json, read as the app's API would answer it: the whole document is
// the AppInfo that Configure returns (features, state, settings, profiles, languages), and
// installedApps beside it is what GetInstalledApps returns. The same file, so the two renderers
// draw the same mocked app and a change to the fixture reaches both.
//
// The fixture was recorded from an older client, and the contract has grown since: a member the
// API now requires and the fixture does not carry is filled here with the value a plain CLIENT
// build has - and every fill is reported, so the drift is visible and the fixture gets re-recorded
// rather than quietly rendered with stand-ins forever.
internal sealed class StoreFixture
{
    public required AppInfo Info { get; init; }
    public required IReadOnlyList<DeviceAppInfo> InstalledApps { get; init; }
    public required IReadOnlyList<string> Filled { get; init; }
    public string? ContractDrift { get; init; }

    public static StoreFixture Load(string path, string? culture)
    {
        var root = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                   ?? throw new InvalidOperationException($"{path} is not a JSON object.");

        var filled = new List<string>();
        FillDefaults(root, filled);
        if (culture != null)
            ApplyCulture(root, culture);

        // strict first: the exception names what the API requires and the fixture lacks
        string? drift = null;
        try {
            root.Deserialize<AppInfo>(Options(strict: true));
        }
        catch (JsonException ex) {
            drift = ex.Message;
        }

        var info = root.Deserialize<AppInfo>(Options(strict: false))
                   ?? throw new InvalidOperationException($"{path} holds no app info.");
        var installedApps = root["installedApps"]?.Deserialize<List<DeviceAppInfo>>(Options(strict: false)) ?? [];

        return new StoreFixture { Info = info, InstalledApps = installedApps, Filled = filled, ContractDrift = drift };
    }

    // The language, the way the app itself reports one: the person's choice in the settings, and the
    // state's current and system cultures - what the view model reads to pick the words, and what the
    // settings page shows. The web UI engine patches only state.currentUiCultureInfo, which is all the
    // SPA reads; the Avalonia UI follows the settings first.
    private static void ApplyCulture(JsonObject root, string culture)
    {
        var cultureInfo = CultureInfo.GetCultureInfo(culture);
        JsonObject Culture() => new() { ["code"] = cultureInfo.Name, ["nativeName"] = cultureInfo.NativeName };

        Section(root, "userSettings")["cultureCode"] = cultureInfo.Name;
        var state = Section(root, "state");
        state["currentUiCultureInfo"] = Culture();
        state["systemUiCultureInfo"] = Culture();
    }

    // What the product IS - the theme it wears, its logo, the consent text it shows, the documents
    // it links to - is never invented here. A fixture without them would render the other product's
    // brand, and that screenshot looks right while showing the wrong app. Every product sets these in
    // its options builder (src/Apps/<product>/<product>/<Product>AppOptions.cs), the two documents from
    // its private appsettings; AvaloniaUI.Dev's Program.cs lists one product's values side by side,
    // which is the easiest place to copy them from.
    private static readonly string[] ProductIdentity =
        ["uiTheme", "logoAssetPath", "privacyConsentAssetName", "privacyPolicyUrl", "termsOfUseUrl"];

    private static void FillDefaults(JsonObject root, List<string> filled)
    {
        var features = Section(root, "features");

        var missing = features.Where(x => x.Value is null).Select(x => x.Key)
            .Concat(ProductIdentity.Where(name => !features.ContainsKey(name)))
            .Intersect(ProductIdentity).Order().ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException(
                $"The fixture does not say which product it is: features has no {string.Join(", ", missing)}. " +
                "Add them from the product's options builder (src/Apps/<product>/<product>/<Product>AppOptions.cs, or " +
                "src/Apps/Tools/AvaloniaUI.Dev/Program.cs for a whole product at once). They are never " +
                "filled in here: a screenshot wearing the other product's theme or logo looks right and is wrong.");

        Fill(features, "companyName", "VpnHood", filled, "features");
        // a premium tier that gates nothing: the fixture's own premiumFeatures: [] in the old shape
        Fill(features, "premium", new JsonObject(), filled, "features");
        Fill(features, "authProviderIds", new JsonArray(), filled, "features");
        Fill(features, "accountWebsiteUrl", null, filled, "features");
        Fill(features, "isLicenseAgreementRequired", false, filled, "features");
        Fill(features, "isRemoteAccessSupported", false, filled, "features");

        var intents = Section(root, "intentFeatures");
        Fill(intents, "isWebBrowserSupported", true, filled, "intentFeatures");

        if (root["clientProfileInfos"] is JsonArray profiles)
            for (var i = 0; i < profiles.Count; i++) {
                if (profiles[i] is not JsonObject profile)
                    continue;
                var where = $"clientProfileInfos[{i}]";
                Fill(profile, "accessCodeRefusal", null, filled, where);
                Fill(profile, "canImportAccessCode", false, filled, where);
                Fill(profile, "canViewAccessCode", false, filled, where);
            }
    }

    private static void Fill(JsonObject obj, string name, JsonNode? value, List<string> filled, string where)
    {
        if (obj.ContainsKey(name))
            return;
        obj[name] = value;
        filled.Add($"{where}.{name}");
    }

    private static JsonObject Section(JsonObject root, string name)
    {
        return root[name] as JsonObject ?? throw new InvalidOperationException($"The fixture has no '{name}' object.");
    }

    // camelCase and string enums, as the app's web server writes them. Lenient drops the `required`
    // on every member so a fixture behind the contract still loads (with the defaults above and CLR
    // defaults for the rest); strict keeps it, to name the drift.
    private static JsonSerializerOptions Options(bool strict)
    {
        var options = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };
        if (!strict)
            options.TypeInfoResolver = new DefaultJsonTypeInfoResolver {
                Modifiers = {
                    typeInfo => {
                        foreach (var property in typeInfo.Properties)
                            property.IsRequired = false;
                    }
                }
            };
        return options;
    }
}
