namespace VpnHood.AppUi.Common;

// What the UI does for one failure: shows the sentence with the buttons beside Close, opens a page
// of its own, or nothing at all.
public sealed record ErrorMessage(string Text, params IReadOnlyList<ErrorAction> Actions)
{
    public ErrorPage Page { get; init; }
    public bool IsIgnored { get; init; }

    public static ErrorMessage Ignored { get; } = new("") { IsIgnored = true };

    public static ErrorMessage OnPage(ErrorPage page)
    {
        return new ErrorMessage("") { Page = page };
    }
}
