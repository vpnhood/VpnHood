namespace VpnHood.Core.Client.VpnServices.Manager.Exceptions;

public class VpnServiceTimeoutException(string message, Exception? innerException = null)
    : TimeoutException(message, innerException)
{
    public required TimeSpan TimeoutDuration {
        get {
            var value = Data["TimeoutDuration"];
            return value is null ? TimeSpan.Zero : TimeSpan.FromSeconds((int)value);
        }
        init { Data["TimeoutDuration"] = (int)value.TotalSeconds; }
    }
}
