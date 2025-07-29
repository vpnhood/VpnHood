namespace VpnHood.Core.Client.Device.UiContexts;

public interface IUiContext
{
    bool IsDestroyed { get; }
    bool IsActive { get; }
};