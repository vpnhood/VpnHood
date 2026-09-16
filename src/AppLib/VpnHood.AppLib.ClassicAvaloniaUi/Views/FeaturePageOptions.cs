namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

// What a feature page shows (FeaturePageLayout.vue's props): the marked-up title, the picture, the
// steps, the button that opens the device's own settings, and whether the feature is sold.
public sealed record FeaturePageOptions
{
    public required string Title { get; init; }
    public string? Description { get; init; }
    public required string Image { get; init; }
    public IReadOnlyList<string> Steps { get; init; } = [];
    public string? ButtonText { get; init; }
    public Func<Task>? Action { get; init; }
    public bool IsPremium { get; init; }
    public bool IsActionAvailable { get; init; }
    public bool ShowSkip { get; init; }
    public FeaturePageKind Kind { get; init; } = FeaturePageKind.Steps;
}
