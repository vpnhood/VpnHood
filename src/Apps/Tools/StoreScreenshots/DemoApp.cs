namespace VpnHood.App.StoreScreenshots;

// One of the apps the Split Apps screen lists.
//
// A real device answers that page with whatever is installed, icons and all. A store screenshot
// cannot: shipping other companies' icons in our listing is their artwork and their trademark on
// our page. So the picture shows well-known app NAMES over plain tiles in the colour each is known
// by - enough for the screen to read as a real phone's app list, with nothing borrowed.
internal sealed class DemoApp
{
    public required string AppId { get; init; }
    public required string AppName { get; init; }

    // One colour, or several for a gradient: what the tile is painted with.
    public required IReadOnlyList<string> Background { get; init; }

    // The letter's colour, one or several.
    public required IReadOnlyList<string> Foreground { get; init; }
}
