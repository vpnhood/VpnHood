using VpnHood.Core.Client.Device.Exceptions;

namespace VpnHood.Core.Client.Device;

public class ActiveUiContext
{
    private static IUiContext? _context;
    public static event EventHandler? OnChanged;

    public static IUiContext? Context {
        get => _context;
        set {
            if (_context == value)
                return;

            _context = value;
            OnChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static IUiContext RequiredContext => Context ?? throw new UiContextNotAvailableException();
}