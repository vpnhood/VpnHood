using System;

namespace VpnHood.AccessServer.Dtos;

public class User
{
    public Guid UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedTime { get; set; }
    public int MaxProjectCount { get; set; }
}