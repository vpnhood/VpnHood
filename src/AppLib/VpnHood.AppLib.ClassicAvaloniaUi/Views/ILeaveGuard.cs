namespace VpnHood.AppLib.ClassicAvaloniaUi.Views;

// A page that may hold the leave: a save on the way out that failed, and says so (the web UI's
// onBeforeRouteLeave returning false).
public interface ILeaveGuard
{
    Task<bool> CanLeave();
}
