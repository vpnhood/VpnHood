using System;

namespace VpnHood.AccessServer.Models;

public class UserModel
{
    public Guid UserId { get; set; }
    public string? AuthUserId { get; set; }
    public string? AuthCode { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedTime { get; set; }
    public int MaxProjectCount { get; set; }
}