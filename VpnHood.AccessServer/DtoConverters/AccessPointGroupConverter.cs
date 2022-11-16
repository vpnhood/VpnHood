using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class AccessPointGroupConverter
{
    public static AccessPointGroup ToDto(this AccessPointGroupModel model)
    {
        var accessPointGroup = new AccessPointGroup
        {
            AccessPointGroupName = model.AccessPointGroupName,
            AccessPointGroupId= model.AccessPointGroupId,
            CertificateId= model.CertificateId,
            CreatedTime= model.CreatedTime
        };
        return accessPointGroup;
    }
}