using VpnHood.AccessServer.Dtos;
using VpnHood.AccessServer.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class CertificateConverter
{
    public static Certificate ToDto(this CertificateModel model, bool withRawData = false)
    {
        var certificate = new Certificate
        {
            CreatedTime = model.CreatedTime,
            CertificateId = model.CertificateId,
            CommonName = model.CommonName,
            ExpirationTime = model.ExpirationTime,
            RawData = withRawData ? model.RawData : null
        };
        return certificate;
    }
}