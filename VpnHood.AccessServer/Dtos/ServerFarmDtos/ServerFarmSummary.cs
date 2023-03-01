namespace VpnHood.AccessServer.Dtos.ServerFarmDto;

public class ServerFarmSummary
{
    public required int TotalTokenCount { get; init; }
    public required int ActiveTokenCount { get; init; }
    public required int InactiveTokenCount { get; init; }
    public required int UnusedTokenCount { get; init; }
    public required int ServerCount { get; init; }
    public required int SessionCount { get; init; }
    public required long TransferSpeed { get; init; }
}

