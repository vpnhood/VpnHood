namespace VpnHood.AppLib.Abstractions;

public record AppSignInOptions
{
    /// <summary>A sign-in method id reported by AppFeatures.SignInMethods (see AppSignInMethods).</summary>
    public required string Method { get; init; }
    public string? UserName { get; init; }
    public string? Password { get; init; }
}
