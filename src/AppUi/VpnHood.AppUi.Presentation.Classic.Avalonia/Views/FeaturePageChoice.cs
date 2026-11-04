namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

// One level of a Choice page's setting: its title, its note, whether the setting is at it now, and what choosing it
// writes.
public sealed record FeaturePageChoice
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public bool IsChosen { get; init; }
    public required Func<Task> Action { get; init; }
}
