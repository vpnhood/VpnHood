namespace VpnHood.Core.Common.Messaging;

public class UserReview
{
    [Obsolete("Use Rating")]
    public int Rate {
        get => Rating;
        set => Rating = value;
    }

    public int Rating { get; set; }
    public DateTime Time { get; init; }
    public Version AppVersion { get; init; } = new();
}