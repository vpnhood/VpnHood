namespace VpnHood.AppUi.Presentation.Classic.Avalonia.Views;

// A page that may hold the leave: a save on the way out that failed, and says so.
public interface ILeaveGuard
{
    Task<bool> CanLeave(CancellationToken cancellationToken);
}
