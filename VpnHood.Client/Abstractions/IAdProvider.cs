namespace VpnHood.Client.Abstractions;

public interface IAdProvider
{
    Task<string> ShowAd(string sessionId, CancellationToken cancellationToken);
}