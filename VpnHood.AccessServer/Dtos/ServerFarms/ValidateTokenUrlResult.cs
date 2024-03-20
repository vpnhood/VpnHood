namespace VpnHood.AccessServer.Dtos.ServerFarms;

public class ValidateTokenUrlResult
{
    public required DateTime? RemoteTokenTime { get; init; }
    public required bool IsUpToDate { get; init; }
    public required string? ErrorMessage { get; init; }
}