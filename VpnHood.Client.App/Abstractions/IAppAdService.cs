namespace VpnHood.Client.App.Abstractions;

public interface IAppAdService : IDisposable
{
    Task ShowAd(IAppUiContext appUiContext, string customData, CancellationToken cancellationToken);
}