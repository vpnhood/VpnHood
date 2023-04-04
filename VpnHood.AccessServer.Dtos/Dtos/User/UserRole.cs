namespace VpnHood.AccessServer.Dtos;

public class UserRole
{
    public required User User { get; set; }
    public required Role Role { get; set; }
    public required string AppId { get; set; }
}