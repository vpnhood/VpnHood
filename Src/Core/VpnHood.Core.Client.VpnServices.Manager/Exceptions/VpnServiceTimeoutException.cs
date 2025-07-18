namespace VpnHood.Core.Client.VpnServices.Manager.Exceptions;

public class VpnServiceTimeoutException : TimeoutException
{
    public VpnServiceTimeoutException(string message) :
        base(message)
    {
    }

    public VpnServiceTimeoutException(string message, Exception innerException) :
        base(message, innerException)
    {
    }

    public required TimeSpan TimeoutDuration
    {
        get
        {
            var value = Data["TimeoutDuration"];
            return value is null ? TimeSpan.Zero : TimeSpan.FromSeconds((int)value);
        }
        init
        {
            Data["TimeoutDuration"] = (int)value.TotalSeconds;
        }
    }
}