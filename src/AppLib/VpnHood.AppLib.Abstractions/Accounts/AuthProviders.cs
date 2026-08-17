namespace VpnHood.AppLib.Abstractions.Accounts;

/// <summary>
/// Well-known sign-in method ids. A sign-in method is a free-form STRING self-declared by the app's
/// IAuthenticationExternalProvider — deliberately not an enum, so a third-party app built on these
/// packages can introduce its own identity provider ("github", "samsung", …) without a change to this
/// library. The id is a contract, lowercase by convention, consumed verbatim everywhere:
///   - the portal sends it as the wire discriminator and the backend keys its token verifiers on it
///     (unknown ids are the backend's to reject — fail-closed);
///   - the WebUI derives its sign-in label key as SIGN_IN_WITH_&lt;UPPERCASE-ID&gt; and falls back to a
///     generic "Sign in" when no such key exists.
/// Changing an id is therefore a breaking change on the wire and in the UI label lookup.
/// </summary>
public static class AuthProviders
{
    public const string Google = "google";
    public const string Apple = "apple";
    public const string Microsoft = "microsoft";

    /// <summary>
    /// The account website's own email + password, checked by the backend against its client login
    /// (no external IdP involved). Sign-in only: the backend never creates an account for this
    /// method. May require a second step — see SignInResult.State.
    /// </summary>
    public const string Password = "password";
}
