using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Authorization.Models
{
    public class ObjectType
    {
        public ObjectType(Guid objectTypeId, string objectTypeName)
        {
            ObjectTypeId = objectTypeId;
            ObjectTypeName = objectTypeName;
        }

        public Guid ObjectTypeId { get; set; }
        public string ObjectTypeName { get; set; }

        [JsonIgnore] public virtual ICollection<SecurityDescriptor>? SecurityDescriptors { get; set; }
    }
}