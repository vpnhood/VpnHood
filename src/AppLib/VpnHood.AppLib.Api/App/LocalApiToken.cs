namespace VpnHood.AppLib.Api.App;

/// <summary>
/// Provides the local web host's token as its address carries it: after <c>#</c>, which a browser never sends, so
/// whatever loads the page hands it the token.
/// </summary>
public static class LocalApiToken
{
    private const string FragmentName = "token";

    /// <summary>Makes the address's fragment that carries a token.</summary>
    /// <param name="token">The token.</param>
    /// <returns>The fragment, as <c>#token=…</c>.</returns>
    public static string Fragment(string token) => $"#{FragmentName}={token}";

    /// <summary>Reads the token an address carries.</summary>
    /// <param name="url">The address.</param>
    /// <returns>The token, or <see langword="null"/> where the address carries none, as a remote listener's.</returns>
    /// <exception cref="InvalidOperationException"><paramref name="url"/> is a relative address.</exception>
    public static string? Read(Uri url)
    {
        var fragment = url.Fragment.TrimStart('#');
        var prefix = FragmentName + "=";
        return fragment.StartsWith(prefix, StringComparison.Ordinal) && fragment.Length > prefix.Length
            ? fragment[prefix.Length..]
            : null;
    }
}
