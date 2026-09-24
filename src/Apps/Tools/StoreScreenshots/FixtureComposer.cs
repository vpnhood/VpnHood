using System.Text.Json.Nodes;

namespace VpnHood.App.StoreScreenshots;

// The app's answers for one shot: the recording, with what this OS build can do laid over it, then
// what this screen needs.
//
// A patch names only the fields that matter, so it merges into the recording rather than replacing
// it - except for a list, which replaces wholesale: a list patched item by item would silently
// keep entries the patch meant to drop (the premium-only location list is exactly that case).
internal static class FixtureComposer
{
    public static JsonObject Compose(JsonObject fixture, JsonObject? platformFile, JsonObject? platformPatch,
        JsonObject? shotPatch, JsonArray? installedApps)
    {
        var composed = (JsonObject)fixture.DeepClone();
        Merge(composed, platformFile);
        Merge(composed, platformPatch);
        Merge(composed, shotPatch);
        if (installedApps != null)
            composed["installedApps"] = installedApps.DeepClone();
        return composed;
    }

    private static void Merge(JsonObject target, JsonObject? patch)
    {
        if (patch == null)
            return;

        foreach (var (key, value) in patch) {
            if (target[key] is JsonObject branch && value is JsonObject patchBranch)
                Merge(branch, patchBranch);
            else
                target[key] = value?.DeepClone();
        }
    }
}
