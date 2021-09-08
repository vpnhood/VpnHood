using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using VpnHood.AccessServer.DTOs;
using VpnHood.AccessServer.Models;
using VpnHood.Server;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/certificates")]
    public class CertificateController : SuperController<CertificateController>
    {
        public CertificateController(ILogger<CertificateController> logger)
            : base(logger)
        {
        }

        [HttpPost]
        public async Task<Certificate> Create(Guid projectId, CertificateCreateParams? createParams)
        {
            await using VhContext vhContext = new();
            var certificate = CreateInternal(projectId, createParams);
            vhContext.Certificates.Add(certificate);
            await vhContext.SaveChangesAsync();
            return certificate;
        }

        internal static Certificate CreateInternal(Guid projectId, CertificateCreateParams? createParams)
        {
            createParams ??= new CertificateCreateParams();
            if (!string.IsNullOrEmpty(createParams.SubjectName) && createParams.RawData?.Length > 0)
                throw new InvalidOperationException(
                    $"Could not set both {createParams.SubjectName} and {createParams.RawData} together!");

            // create cert
            var certificateRawBuffer = createParams.RawData?.Length > 0
                ? createParams.RawData
                : CertificateUtil.CreateSelfSigned(createParams.SubjectName).Export(X509ContentType.Pfx);

            // add cert into 
            X509Certificate2 x509Certificate2 = new(certificateRawBuffer, createParams.Password,
                X509KeyStorageFlags.Exportable);
            certificateRawBuffer = x509Certificate2.Export(X509ContentType.Pfx); //removing password

            Certificate ret = new()
            {
                CertificateId = Guid.NewGuid(),
                ProjectId = projectId,
                RawData = certificateRawBuffer,
                CommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false),
                ExpirationTime = x509Certificate2.NotAfter.ToUniversalTime(),
                CreatedTime = DateTime.UtcNow
            };

            return ret;
        }


        [HttpGet("{certificateId:guid}")]
        public async Task<Certificate> Get(Guid projectId, Guid certificateId)
        {
            await using VhContext vhContext = new();
            var certificate = await vhContext.Certificates.SingleAsync(x => x.ProjectId == projectId && x.CertificateId == certificateId);
            return certificate;
        }

        [HttpDelete("{certificateId:guid}")]
        public async Task Delete(Guid projectId, Guid certificateId)
        {
            await using VhContext vhContext = new();
            var certificate = await vhContext.Certificates
                .SingleAsync(x => x.ProjectId == projectId && x.CertificateId == certificateId);
            vhContext.Certificates.Remove(certificate);
            await vhContext.SaveChangesAsync();
        }

        [HttpPatch("{certificateId:guid}")]
        public async Task<Certificate> Update(Guid projectId, Guid certificateId, CertificateUpdateParams updateParams)
        {
            await using VhContext vhContext = new();
            var certificate = await vhContext.Certificates
                .SingleAsync(x => x.ProjectId == projectId && x.CertificateId == certificateId);

            if (updateParams.RawData != null)
            {
                X509Certificate2 x509Certificate2 = new(updateParams.RawData, updateParams.Password?.Value, X509KeyStorageFlags.Exportable);
                certificate.CommonName = x509Certificate2.GetNameInfo(X509NameType.DnsName, false);
                certificate.RawData = x509Certificate2.Export(X509ContentType.Pfx);
            }

            await vhContext.SaveChangesAsync();
            return certificate;
        }

        [HttpGet]
        public async Task<Certificate[]> List(Guid projectId, int recordIndex = 0, int recordCount = 300)
        {
            await using VhContext vhContext = new();
            var query = vhContext.Certificates.Where(x => x.ProjectId == projectId);
            var res = await query
                .Skip(recordIndex)
                .Take(recordCount)
                .ToArrayAsync();

            foreach (var item in res)
                item.RawData = Array.Empty<byte>();

            return res;
        }
    }
}