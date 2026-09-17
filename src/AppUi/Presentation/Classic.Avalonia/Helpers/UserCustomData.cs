using System.Text.Json;
using VpnHood.AppUi.Hosting.Avalonia;

namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Helpers;

// The UI's own flags in UserSettings.CustomData, the bag the web UI keeps its
// enableAutoRefreshProxyList in: read and written by the same names, so both UIs see one setting.
internal static class UserCustomData
{
    public static bool GetBool(string key)
    {
        var data = AppModel.UserSettings.CustomData;
        return data is { ValueKind: JsonValueKind.Object } obj
               && obj.TryGetProperty(key, out var value)
               && value.ValueKind == JsonValueKind.True;
    }

    public static Task SetBool(string key, bool value, CancellationToken cancellationToken)
    {
        var settings = AppModel.UserSettings;
        var bag = settings.CustomData is { ValueKind: JsonValueKind.Object } obj
            ? obj.EnumerateObject().ToDictionary(x => x.Name, x => x.Value)
            : new Dictionary<string, JsonElement>();
        bag[key] = JsonSerializer.SerializeToElement(value, CustomDataJsonContext.Default.Boolean);
        settings.CustomData = JsonSerializer.SerializeToElement(bag, CustomDataJsonContext.Default.DictionaryStringJsonElement);
        return AppModel.SaveUserSettings(settings, cancellationToken);
    }
}
