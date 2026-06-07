namespace VpnHood.Core.Client.Devices.UiContexts;

public interface IUiContext
{
    Task<bool> IsDestroyed();
    Task<bool> IsActive();
};