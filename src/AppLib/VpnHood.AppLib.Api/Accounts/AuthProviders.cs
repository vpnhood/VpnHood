
namespace VpnHood.AppLib.Api.Accounts;

/// <summary>Provides the IDs of the well-known sign-in methods.</summary>
/// <remarks>
/// A sign-in method's ID is a string the app's sign-in provider declares, so an app may bring a method of its own. The
/// ID is a contract, in lower case: the account backend checks a sign-in by it, and the UI names its button by the key
/// <c>SIGN_IN_WITH_</c> and the ID in upper case, or "Sign in" where it has no such key, so changing an ID breaks both.
/// </remarks>
public static class AuthProviders
{
    /// <summary>The ID of the sign-in with Google.</summary>
    public const string Google = "google";

    /// <summary>The ID of the sign-in with Apple.</summary>
    public const string Apple = "apple";

    /// <summary>The ID of the sign-in with Microsoft.</summary>
    public const string Microsoft = "microsoft";

    /// <summary>The ID of the sign-in with the account website's email address and password.</summary>
    /// <remarks>
    /// It signs in only, never creating an account, and may need a second step (<see cref="SignInResult.State"/>).
    /// </remarks>
    public const string Password = "password";
}
