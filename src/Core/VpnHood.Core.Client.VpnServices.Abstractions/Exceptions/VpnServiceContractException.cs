namespace VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

// The app and its VPN engine share no version of the contract between them: a build that paired an engine
// with an app of another time, which only its developer meets.
public class VpnServiceContractException(string message)
    : VpnServiceException(message);
