namespace VpnHood.AppLib.Api.Exceptions;

/// <summary>Represents the diagnose's answer that it found no issue.</summary>
public class NoErrorFoundException() : Exception("Diagnoser has finished and no issue has been detected.");