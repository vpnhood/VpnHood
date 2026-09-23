namespace VpnHood.Core.Client.Devices.Abstractions.UiContexts;

public interface IUiContext
{
    Task<bool> IsDestroyed();
    Task<bool> IsActive();
};