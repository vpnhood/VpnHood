using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace VpnHood.AccessServer.Auth.Models
{
    public class SecurityDescriptor
    {
        public int SecurityDescriptorId { get; set; }
        public int? ParentSecurityDescriptorId { get; set; }
        public int ObjectTypeId { get; set; }
        public Guid ObjectId { get; set; }
    }
}