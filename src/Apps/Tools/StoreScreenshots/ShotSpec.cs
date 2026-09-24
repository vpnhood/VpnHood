using System.Text.Json.Nodes;

namespace VpnHood.App.StoreScreenshots;

// One screen of a store's set.
//
// Array order IS the store's order - the file is numbered by where the shot sits in the list - so
// reordering a set is moving items, never renumbering them.
internal sealed class ShotSpec
{
    // The page, by the route the UI knows it as (PageRoutes). "/" is the page the app opens on.
    public required string Route { get; init; }

    // What this screen is, for the run's log and for whoever reads the list.
    public required string Label { get; init; }

    // What the app must be showing for this screen to be worth a listing: a session, a setting
    // turned on, a list emptied. Merged over the fixture after the platform's own patch.
    public JsonObject? Patch { get; init; }

    // Controls that must not be in this picture, each a path of names from the page down
    // ("EnabledItem/WarningChip"). A path that matches nothing fails the run rather than quietly
    // shipping the control.
    public IReadOnlyList<string> Hide { get; init; } = [];

    // A picture that is not a screen at all - a poster, a title card - taken from the store repo
    // instead of drawn. Given as a path inside the configuration's own folder.
    public string? Source { get; init; }

    // Filled in by the run: the shot's place in its platform's list, which is its file's number.
    public string Number { get; set; } = "";
}
