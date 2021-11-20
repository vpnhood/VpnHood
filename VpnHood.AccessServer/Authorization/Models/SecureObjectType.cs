using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VpnHood.AccessServer.Authorization.Models
{
    public class SecureObjectType
    {
        public SecureObjectType(Guid secureObjectTypeId, string secureObjectTypeName)
        {
            SecureObjectTypeId = secureObjectTypeId;
            SecureObjectTypeName = secureObjectTypeName;
        }

        public Guid SecureObjectTypeId { get; set; }
        public string SecureObjectTypeName { get; set; }

        [JsonIgnore] public virtual ICollection<SecureObject>? SecureObjects { get; set; }
    }
}