using VpnHood.AccessServer.Dtos.Certificate;
using VpnHood.AccessServer.Persistence.Models;

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
            DnsVerificationText = model.DnsVerificationText,
            SubjectName = model.SubjectName,
            RawData = withRawData ? model.RawData : null
        };
        return certificate;
    }
}