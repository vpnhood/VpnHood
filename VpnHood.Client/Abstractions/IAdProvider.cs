namespace VpnHood.Client.Abstractions;

public interface IAdProvider
{
    Task<ShowedAdResult> ShowAd(string sessionId, CancellationToken cancellationToken);
}