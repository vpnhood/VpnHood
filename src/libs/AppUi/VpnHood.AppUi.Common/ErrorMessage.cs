namespace VpnHood.AppUi.Common;

// What the UI does about one failure. A failure ends in exactly one of three ways, so they are
// three types rather than three flags on one: nothing can be silenced AND carry buttons, and a
// page cannot arrive with a sentence nobody will read. Only this file can add a fourth - the
// private constructor closes the set - so a UI that handles these three handles all of them.

/// <summary>
/// Represents what the UI does about one failure, as one of the three cases provided: <see cref="Ignored"/>,
/// <see cref="Page"/> or <see cref="Dialog"/>.
/// </summary>
public abstract record ErrorMessage
{
    private ErrorMessage()
    {
    }

    /// <summary>Represents a failure that was a cancel, as the person's own, so there is nothing to tell them.</summary>
    public sealed record Ignored : ErrorMessage;

    /// <summary>
    /// Represents a failure that opens a page of its own, as what has to be said does not fit in a dialog.
    /// </summary>
    /// <param name="Target">The page.</param>
    public sealed record Page(ErrorPage Target) : ErrorMessage;

    /// <summary>Represents a failure told in a dialog: the sentence, and the buttons it earned beside Close.</summary>
    /// <param name="Text">The sentence.</param>
    /// <param name="Actions">The buttons beside Close.</param>
    public sealed record Dialog(string Text, params IReadOnlyList<ErrorAction> Actions) : ErrorMessage;
}
