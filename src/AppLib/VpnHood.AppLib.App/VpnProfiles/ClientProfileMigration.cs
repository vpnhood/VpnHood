using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.VpnProfiles;

// VpnProfile was named ClientProfile, and a file saved before the rename still carries the old keys.
[Obsolete("Migration (added 2026-09-26): remove after 2027-03, with its two callers and the " +
          "Load_files_saved_before_the_VpnProfile_rename test.")]
internal static class ClientProfileMigration
{
    public static void MigrateProfiles(string filePath)
    {
        Migrate(filePath, root => root.AsArray().OfType<JsonObject>(), writeIndented: false);
    }

    public static void MigrateSettings(string filePath)
    {
        Migrate(filePath, root => root["UserSettings"] is JsonObject userSettings ? [userSettings] : [],
            writeIndented: true);
    }

    private static void Migrate(string filePath, Func<JsonNode, IEnumerable<JsonObject>> getOwners,
        bool writeIndented)
    {
        try {
            if (!File.Exists(filePath))
                return;

            var json = File.ReadAllText(filePath);
            if (!json.Contains("\"ClientProfile"))
                return;

            var root = JsonNode.Parse(json) ?? throw new InvalidDataException("The file holds no JSON.");
            foreach (var owner in getOwners(root)) {
                RenameKey(owner, "ClientProfileId", "VpnProfileId");
                RenameKey(owner, "ClientProfileName", "VpnProfileName");
            }

            File.WriteAllText(filePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = writeIndented }));
            VhLogger.Instance.LogInformation("Renamed the ClientProfile keys. FilePath: {FilePath}", filePath);
        }
        catch (Exception ex) {
            // the load that follows reports an unreadable file its own way
            VhLogger.Instance.LogError(ex, "Could not rename the ClientProfile keys. FilePath: {FilePath}", filePath);
        }
    }

    private static void RenameKey(JsonObject owner, string oldName, string newName)
    {
        if (!owner.TryGetPropertyValue(oldName, out var value))
            return;

        owner.Remove(oldName);
        owner[newName] = value;
    }
}
