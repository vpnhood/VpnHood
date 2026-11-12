namespace VpnHood.Core.Common.Messaging;

/// <summary>
/// Represents a user's review of the app.
/// </summary>
public class UserReview
{
    /// <summary>Gets or sets the rating: the same value as <see cref="Rating"/>, for older readers.</summary>
    [Obsolete("Use Rating")]
    public int Rate {
        get => Rating;
        set => Rating = value;
    }

    /// <summary>Gets or sets the rating the user gave.</summary>
    public int Rating { get; set; }

    /// <summary>Gets when the user gave it.</summary>
    public DateTime Time { get; init; }

    /// <summary>Gets the version of the app the user reviewed. The default is 0.0.</summary>
    public Version AppVersion { get; init; } = new();
}