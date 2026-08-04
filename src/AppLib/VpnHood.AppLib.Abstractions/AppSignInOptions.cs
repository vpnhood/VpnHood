namespace VpnHood.AppLib.Abstractions;

public record AppSignInOptions
{
    public required AppSignInMethod Method { get; init; }
    public string? UserName { get; init; }
    public string? Password { get; init; }
}
