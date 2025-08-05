namespace VpnHood.Core.Client.Device.UiContexts;

public interface IUiContext
{
    Task<bool> IsDestroyed();
    Task<bool> IsActive();
};