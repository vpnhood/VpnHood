using VpnHood.AccessServer.Dtos.Certificates;
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
            AutoValidate = model.AutoValidate,
            ValidateInprogress = model.ValidateInprogress,
            ValidateCount = model.ValidateCount,
            ValidateError = model.ValidateError,
            ValidateErrorTime = model.ValidateErrorTime,
            ValidateErrorCount = model.ValidateErrorCount
        };
        return certificate;
    }
}