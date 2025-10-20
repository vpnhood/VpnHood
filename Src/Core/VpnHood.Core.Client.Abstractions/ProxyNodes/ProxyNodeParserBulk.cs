using System.Text.RegularExpressions;

namespace VpnHood.Core.Client.Abstractions.ProxyNodes;

internal static class ProxyNodeParserBulk
{
    /// <summary>
    /// Parse heterogeneous proxy lines into Uri[].
    /// </summary>
    /// <param name="lines">Enumerable of raw lines (each line may contain host, port and metadata).</param>
    /// <param name="defaultScheme">Scheme to use when none is found (default "http").</param>
    /// <param name="preferHttpsWhenPort443">If true and no scheme present but port == 443, use https.</param>
    /// <param name="acceptedSchemes">Optional additional allowed schemes (e.g. socks4, socks5).</param>
    /// <returns>Array of parsed Uri objects (unique by original string).</returns>
    public static Uri[] Parse(
        IEnumerable<string> lines,
        string defaultScheme = "http",
        bool preferHttpsWhenPort443 = true,
        IEnumerable<string>? acceptedSchemes = null)
    {

        // ReSharper disable once CollectionNeverQueried.Local
        var errors = new List<string>();
        var results = new List<Uri>();
        var schemes = new HashSet<string>((acceptedSchemes ?? ["http", "https", "socks4", "socks5"]).Select(s => s.ToLowerInvariant()));

        // Regex that matches:
        // [scheme://][user[:pass]@][host or [ipv6]][:port]
        // We'll search the line for the first occurrence.
        var re = new Regex(@"\b(?:(?<scheme>[a-zA-Z][a-zA-Z0-9+\-.]*)://)?(?:(?<user>[^:@\s\/?#]+)(?::(?<pass>[^@\s\/?#]*))?@)?(?<host>\[[^\]]+\]|[A-Za-z0-9\.\-]+)(?::(?<port>\d{1,5}))?\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        var lineNo = 0;
        foreach (var raw in lines) {
            lineNo++;
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line)) {
                errors.Add($"Line {lineNo}: empty or null");
                continue;
            }

            var m = re.Match(line);
            if (!m.Success) {
                // fallback: sometimes host:port appears separated by whitespace or tabs as a token
                // try tokenizing and find token with pattern host:port or user:pass@host:port or URL-like
                var tokens = line.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
                var found = false;
                foreach (var t in tokens) {
                    var mm = re.Match(t);
                    if (mm.Success) {
                        m = mm;
                        found = true;
                        break;
                    }
                }

                if (!found) {
                    errors.Add($"Line {lineNo}: no host:port or URL found -> '{line}'");
                    continue;
                }
            }

            try {
                var scheme = m.Groups["scheme"].Success ? m.Groups["scheme"].Value.ToLowerInvariant() : null;
                var user = m.Groups["user"].Success ? m.Groups["user"].Value : null;
                var pass = m.Groups["pass"].Success ? m.Groups["pass"].Value : null;
                var host = m.Groups["host"].Value;
                var portStr = m.Groups["port"].Success ? m.Groups["port"].Value : null;

                // normalize IPv6 bracketed host for UriBuilder usage
                var hostIsIpv6Bracketed = host.StartsWith("[") && host.EndsWith("]");
                var hostForUri = hostIsIpv6Bracketed ? host : host;

                var port = -1;
                if (!string.IsNullOrEmpty(portStr)) {
                    if (!int.TryParse(portStr, out port) || port < 1 || port > 65535) {
                        errors.Add($"Line {lineNo}: invalid port '{portStr}' in '{line}'");
                        continue;
                    }
                }

                // Determine scheme
                var chosenScheme = scheme;
                if (string.IsNullOrEmpty(chosenScheme)) {
                    if (port == 443 && preferHttpsWhenPort443)
                        chosenScheme = "https";
                    else
                        chosenScheme = defaultScheme;
                }

                // Build the authority (credentials + host + port) safely using UriBuilder
                var ub = new UriBuilder {
                    Scheme = chosenScheme,
                    Host = hostIsIpv6Bracketed ? host.Trim('[', ']') : hostForUri // UriBuilder expects bare host (no brackets)
                };

                if (port != -1)
                    ub.Port = port;
                else
                    ub.Port = -1; // leave default port for scheme

                // set credentials by inserting them into the UserInfo of the UriBuilder
                if (!string.IsNullOrEmpty(user)) {
                    // User and password must be percent-encoded in the URI.
                    // Use Uri.EscapeDataString (not EscapeUriString) to be safe for userinfo.
                    var encUser = Uri.EscapeDataString(user);
                    var encPass = pass != null ? Uri.EscapeDataString(pass) : null;
                    ub.UserName = encUser;   // note: UriBuilder will include UserInfo automatically when ToString() is used
                    // UriBuilder doesn't have direct Password property in older frameworks - we set the UserName and build UserInfo manually:
                    if (encPass != null) 
                        ub.Password = encPass; // available in .NET 6+
                }

                // Create final Uri carefully.
                // To ensure credentials are included and properly encoded across frameworks, build string manually if needed.
                var finalUri = ub.Uri;
                results.Add(finalUri);
            }
            catch (Exception ex) {
                errors.Add($"Line {lineNo}: exception parsing '{line}': {ex.Message}");
            }
        }

        // Remove duplicates (string equality)
        var unique = results
            .GroupBy(u => u.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();

        return unique;
    }
}