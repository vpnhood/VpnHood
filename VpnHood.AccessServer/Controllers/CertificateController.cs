using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Threading.Tasks;
using VpnHood.AccessServer.Services;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class CertificateController : SuperController<CertificateController>
    {

        public CertificateController(ILogger<CertificateController> logger) : base(logger)
        {
        }

        public Task Import(string serverEndPoint, byte[] rawData, string password = null)
        {
            Authorize(App.AdminUserId);

            return CertificateService.Create(serverEndPoint: serverEndPoint, rawData: rawData, password: password);
        }

        public Task Delete(string serverEndPoint)
        {
            Authorize(App.AdminUserId);

            var certificateService = CertificateService.FromId(serverEndPoint);
            return certificateService.Delete();
        }

        public Task Create(string serverEndPoint, string subjectName)
        {
            Authorize(App.AdminUserId);
            return CertificateService.Create(serverEndPoint, subjectName);
        }
    }
}
