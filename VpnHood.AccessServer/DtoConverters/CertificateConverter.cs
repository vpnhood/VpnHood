using VpnHood.AccessServer.Dtos.Certificate;
using VpnHood.AccessServer.Persistence.Models;

namespace VpnHood.AccessServer.DtoConverters;

public static class CertificateConverter
{
    public static Certificate ToDto(this CertificateModel model)
    {
        var certificate = new Certificate
        {
            CreatedTime = model.CreatedTime,
            CertificateId = model.CertificateId,
            CommonName = model.CommonName,
            IssueTime = model.IssueTime,
            ExpirationTime = model.ExpirationTime,
            IsTrusted = model.IsTrusted,
            Thumbprint = model.Thumbprint,
            SubjectName = model.SubjectName,
            AutoRenew = model.AutoRenew,
            RenewInprogress = model.RenewInprogress,
            RenewCount = model.RenewCount,
            RenewError = model.RenewError,
            RenewErrorTime = model.RenewErrorTime,
            RenewErrorCount = model.RenewErrorCount,
        };
        return certificate;
    }
}