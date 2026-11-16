using VpnHood.Core.Common.Tokens;

namespace VpnHood.Core.Client.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when no server at the location asked for can be reached.
/// </summary>
/// <param name="message">The message, or <see langword="null"/> for a standard one.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class UnreachableServerLocationException(string? message = null, Exception? innerException = null)
    : UnreachableServerException(message, innerException)
{
    /// <summary>
    /// Creates the exception for a location, with the location in its message, unless it is the automatic one, and in
    /// its <see cref="Exception.Data"/>.
    /// </summary>
    /// <param name="serverLocation">The location asked for, or <see langword="null"/> for the automatic one.</param>
    /// <returns>The exception.</returns>
    public static UnreachableServerLocationException Create(string? serverLocation)
    {
        var isAutoLocation = ServerLocationInfo.IsAutoLocation(serverLocation);
        var msg = isAutoLocation
            ? "There is no reachable server at this moment. Please try again later."
            : $"There is no reachable server at this moment. Please try again later. Location: {serverLocation}";
        
        var ex = new UnreachableServerLocationException(msg);
        ex.Data.Add("ServerLocation", serverLocation);
        ex.Data.Add("IsAutoLocation", ServerLocationInfo.IsAutoLocation(serverLocation));
        return ex;
    }
}
