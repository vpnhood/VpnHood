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
            IssueTime = model.IssueTime,
            ExpirationTime = model.ExpirationTime,
            IsVerified = model.IsVerified,
            Thumbprint = model.Thumbprint,
            RawData = withRawData ? model.RawData : null
        };
        return certificate;
    }
}