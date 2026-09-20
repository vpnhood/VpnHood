namespace VpnHood.AppUi.Common;

// What an error dialog shows for one failure: the sentence, the buttons, or nothing at all.
public sealed record ErrorMessage(string Text, ErrorActions? Actions = null, bool IsIgnored = false)
{
    public static ErrorMessage Ignored { get; } = new("", IsIgnored: true);
}
