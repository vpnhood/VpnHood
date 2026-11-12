namespace VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when the VPN service does not start in time.
/// </summary>
/// <param name="message">The message.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class VpnServiceTimeoutException(string message, Exception? innerException = null)
    : TimeoutException(message, innerException)
{
    /// <summary>
    /// Gets how long the wait was, kept in <see cref="Exception.Data"/> in whole seconds; zero where it is not there.
    /// </summary>
    public required TimeSpan TimeoutDuration {
        get {
            var value = Data["TimeoutDuration"];
            return value is null ? TimeSpan.Zero : TimeSpan.FromSeconds((int)value);
        }
        init { Data["TimeoutDuration"] = (int)value.TotalSeconds; }
    }
}
