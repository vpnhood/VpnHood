using VpnHood.Client.Device.Exceptions;

namespace VpnHood.Client.Device;

public class ActiveUiContext : IUiContext
{
    public static IUiContext? Context { get; set; }
    public static IUiContext RequiredContext => Context ?? throw new UiContextNotAvailableException();
}