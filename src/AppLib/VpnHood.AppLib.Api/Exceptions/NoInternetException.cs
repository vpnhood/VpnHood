namespace VpnHood.AppLib.Api.Exceptions;

/// <summary>Represents the error of a device with no internet, or too slow a connection.</summary>
public class NoInternetException()
    : Exception("It looks like your device is not connected to the Internet or the connection is too slow.");