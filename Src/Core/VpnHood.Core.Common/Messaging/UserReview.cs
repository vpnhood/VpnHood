namespace VpnHood.Core.Common.Messaging;

public class UserReview
{
    public int Rate { get; init; }
    public DateTime Time { get; init; }
    public Version AppVersion { get; init; } = new();
}