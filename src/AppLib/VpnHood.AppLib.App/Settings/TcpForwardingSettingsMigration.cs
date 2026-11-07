using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Storages;

namespace VpnHood.AppLib.App.Settings;

// TCP forwarding was named TcpProxy, and a file saved before the rename still carries the old keys; a lost UseTcpProxy
// would turn the user's switch off without a word. It goes through the storage, not the file, as tvOS keeps the app's
// files in its defaults.
[Obsolete("Migration (added 2026-11-07): remove after 2027-05, with its caller and the " +
          "Load_settings_saved_before_the_TcpForwarding_rename test.")]
internal static class TcpForwardingSettingsMigration
{
    public static void MigrateSettings(Storage storage, string name)
    {
        try {
            var json = storage.Read(name);
            if (json == null || !json.Contains("TcpProxy"))
                return;

            var root = JsonNode.Parse(json) ?? throw new InvalidDataException("The file holds no JSON.");
            if (root["UserSettings"] is not JsonObject userSettings)
                return;

            var isRenamed = RenameKey(userSettings, "UseTcpProxy", "UseTcpForwarding");
            isRenamed |= RenameKey(userSettings, "IsTcpProxyPrompted", "IsTcpForwardingPrompted");
            if (!isRenamed)
                return;

            storage.Write(name, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            VhLogger.Instance.LogInformation("Renamed the TcpProxy keys of the settings.");
        }
        catch (Exception ex) {
            // the load that follows reports an unreadable file its own way
            VhLogger.Instance.LogError(ex, "Could not rename the TcpProxy keys of the settings.");
        }
    }

    // a key under both names keeps the new one's value
    private static bool RenameKey(JsonObject owner, string oldName, string newName)
    {
        if (!owner.TryGetPropertyValue(oldName, out var value))
            return false;

        owner.Remove(oldName);
        if (!owner.ContainsKey(newName))
            owner[newName] = value;

        return true;
    }
}
