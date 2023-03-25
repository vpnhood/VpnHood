using GrayMint.Common.AspNetCore.SimpleUserManagement.Dtos;
using System;

namespace VpnHood.AccessServer.Dtos.UserDtos
{
    public class User
    {
        public required Guid UserId { get; set; }
        public required string Email { get; set; }
        public required string? FirstName { get; set; }
        public required string? LastName { get; set; }
        public required string? Description { get; set; }
        public required DateTime CreatedTime { get; set; }
        public required DateTime? AccessedTime { get; set; }
        public required bool IsBot { get; set; }
    }
}
