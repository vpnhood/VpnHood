using System.Text.Json.Serialization;

namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Represents what a session has used of its access, and what is left.
/// </summary>
public class AccessUsage
{
    /// <summary>Gets or sets a value indicating whether the session can be extended by watching a rewarded ad.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool CanExtendByRewardedAd { get; set; }

    /// <summary>Gets or sets whether a review was recommended, for older clients; see <see cref="UserReviewRecommended"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [Obsolete]
    public bool IsUserReviewRecommended { get; set; }

    /// <summary>
    /// Gets or sets whether the server recommends that the app asks its user for a review after the session: above 0
    /// if so, 0 if not.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int UserReviewRecommended { get; set; }

    /// <summary>Gets or sets a value indicating whether the session is premium.</summary>
    public bool IsPremium { get; set; }

    /// <summary>Gets or sets the access's traffic in the current cycle.</summary>
    public Traffic CycleTraffic { get; set; } = new();

    /// <summary>
    /// Gets or sets the access's traffic in all; zero where the access manager does not report it, as the file access
    /// manager does not.
    /// </summary>
    public Traffic TotalTraffic { get; set; } = new();

    /// <summary>
    /// Gets or sets the access's traffic quota, kept for older clients: MANAGER reports its cycle's quota where one is
    /// set, else its total's, else 0.
    /// </summary>
    public long MaxTraffic { get; set; }

    /// <summary>
    /// Gets or sets when the session expires, in UTC, or <see langword="null"/> where the access manager reports no
    /// time. MANAGER gives the sooner of the session's end and its access's; the file access manager gives the
    /// session's alone.
    /// </summary>
    public DateTime? ExpirationTime { get; set; }

    /// <summary>
    /// Gets or sets the number of clients using the access now, or <see langword="null"/> where not told; for a public
    /// access, MANAGER reports 1 in the session's first reply and 0 after.
    /// </summary>
    public int? ActiveClientCount { get; set; }
}