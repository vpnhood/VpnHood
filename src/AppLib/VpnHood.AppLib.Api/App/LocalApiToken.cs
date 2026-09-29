namespace VpnHood.AppLib.Api.App;

// The local web host's token as its address carries it: after "#", which a browser never sends, so
// whatever loads the page hands it the token.
public static class LocalApiToken
{
    public const string FragmentName = "token";

    public static string Fragment(string token) => $"#{FragmentName}={token}";

    // Null when the address carries none: a remote listener's, or an address from before the token.
    public static string? Read(Uri url)
    {
        var fragment = url.Fragment.TrimStart('#');
        var prefix = FragmentName + "=";
        return fragment.StartsWith(prefix, StringComparison.Ordinal) && fragment.Length > prefix.Length
            ? fragment[prefix.Length..]
            : null;
    }
}
