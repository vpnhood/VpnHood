using System;

namespace VpnHood.AccessServer.Auth.Models
{
    public class SecurityDescriptor
    {
        public Guid SecurityDescriptorId { get; set; }
        public Guid ObjectId { get; set; }
        public int ObjectTypeId { get; set; }
        public Guid? ParentSecurityDescriptorId { get; set; }
    }
}