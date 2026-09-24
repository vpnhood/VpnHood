namespace VpnHood.AppUi.Common;

// What the UI does about one failure. A failure ends in exactly one of three ways, so they are
// three types rather than three flags on one: nothing can be silenced AND carry buttons, and a
// page cannot arrive with a sentence nobody will read. Only this file can add a fourth - the
// private constructor closes the set - so a UI that handles these three handles all of them.
public abstract record ErrorMessage
{
    private ErrorMessage()
    {
    }

    // said by the person themselves, so there is nothing to tell them: their own cancel, a store's
    // "user cancelled"
    public sealed record Ignored : ErrorMessage;

    // a page of its own, because what has to be said does not fit in a dialog and what has to be
    // done is not a button
    public sealed record Page(ErrorPage Target) : ErrorMessage;

    // the sentence, and the buttons the failure earned beside Close
    public sealed record Dialog(string Text, params IReadOnlyList<ErrorAction> Actions) : ErrorMessage;
}
