namespace VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when the app and its VPN engine share no version of the contract between them: a build
/// that paired an engine with an app of another time, which only its developer meets.
/// </summary>
/// <param name="message">The message.</param>
public class VpnServiceContractException(string message)
    : VpnServiceException(message);
