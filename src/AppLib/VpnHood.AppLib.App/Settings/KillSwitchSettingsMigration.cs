using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using VpnHood.AppLib.Api.Settings;
using VpnHood.Net.Toolkit.Logging;

namespace VpnHood.AppLib.App.Settings;

// 9.0.863 saved the kill switch as two switches, KillSwitch and KillSwitchLockdown. Its bool where the level is now
// fails the whole file's read, which puts every setting back to its default; and a lockdown read as the switch
// alone would lift its block at the next disconnect.
[Obsolete("Migration (added 2026-11-04): remove after 2027-05, with its caller and the " +
          "Load_settings_saved_with_the_two_kill_switch_switches test.")]
internal static class KillSwitchSettingsMigration
{
    private const string LockdownKey = "KillSwitchLockdown";

    public static void MigrateSettings(string filePath)
    {
        try {
            if (!File.Exists(filePath))
                return;

            var json = File.ReadAllText(filePath);
            if (!json.Contains($"\"{LockdownKey}\""))
                return;

            var root = JsonNode.Parse(json) ?? throw new InvalidDataException("The file holds no JSON.");
            if (root["UserSettings"] is not JsonObject userSettings ||
                userSettings["KillSwitch"] is not JsonValue killSwitch ||
                !killSwitch.TryGetValue<bool>(out var isOn))
                return;

            var isLockdown = userSettings[LockdownKey] is JsonValue lockdown &&
                             lockdown.TryGetValue<bool>(out var isLockdownOn) && isLockdownOn;
            var mode = !isOn ? KillSwitchMode.Off
                : isLockdown ? KillSwitchMode.Lockdown
                : KillSwitchMode.UntilDisconnect;
            userSettings["KillSwitch"] = mode.ToString();
            userSettings.Remove(LockdownKey);

            File.WriteAllText(filePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            VhLogger.Instance.LogInformation("Moved the kill switch's two switches to its level. Level: {Level}", mode);
        }
        catch (Exception ex) {
            // the load that follows reports an unreadable file its own way
            VhLogger.Instance.LogError(ex, "Could not move the kill switch's two switches to its level. FilePath: {FilePath}",
                filePath);
        }
    }
}
