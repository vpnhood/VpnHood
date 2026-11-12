namespace VpnHood.AppLib.Api.Settings;

// Where a proxy list is fetched from and how it is pruned. Saved with the rest of the settings, so
// the names here are the settings file's names as much as the wire's.

/// <summary>
/// Provides the options of a managed proxy list's update from a URL.
/// </summary>
public class ProxyAutoUpdateOptions
{
    /// <summary>
    /// Gets or sets the URL of the text to read proxy addresses from, or <see langword="null"/> for no update.
    /// </summary>
    public Uri? Url { get; set; }

    /// <summary>
    /// Gets or sets how often the list is downloaded, the first time as soon as the connector starts, or
    /// <see langword="null"/>, zero or less for no update.
    /// </summary>
    public TimeSpan? Interval { get; set; }

    /// <summary>
    /// Gets or sets the penalty above which a used endpoint goes after the new ones when the lists are merged, or
    /// <see langword="null"/> for 50.
    /// </summary>
    public int? MaxPenalty { get; set; }

    /// <summary>
    /// Gets or sets how many endpoints the merged list keeps, or <see langword="null"/> for
    /// 1,000.
    /// </summary>
    public int? MaxItemCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether endpoints of one host and protocol are thinned out: a merge drops a new
    /// endpoint whose host and protocol match a listed one whose last use succeeded, and a connection that succeeds
    /// disables the others of its host and protocol among those it could choose. The default is
    /// <see langword="true"/>.
    /// </summary>
    public bool RemoveDuplicateIps { get; set; } = true;
}
