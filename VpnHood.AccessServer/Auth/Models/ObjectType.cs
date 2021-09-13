using System.ComponentModel.DataAnnotations.Schema;

namespace VpnHood.AccessServer.Auth.Models
{
    public class ObjectType
    {
        public int ObjectTypeId { get; set; }
        public string ObjectTypeName { get; set; } = null!;
    }
}