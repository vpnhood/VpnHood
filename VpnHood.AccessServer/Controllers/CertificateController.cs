using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Data.SqlClient;
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

        [HttpPost]
        [Route(nameof(Import))]
        [Authorize(AuthenticationSchemes="auth", Roles = "Admin")]
        public Task Import(string serverEndPoint, byte[] rawData, string password = null)
        {
            return CertificateService.Create(serverEndPoint: serverEndPoint, rawData: rawData, password: password);
        }

        [HttpDelete]
        [Route(nameof(Delete))]
        [Authorize(AuthenticationSchemes="auth", Roles = "Admin")]
        public Task Delete(string serverEndPoint)
        {
            var certificateService = CertificateService.FromId(serverEndPoint);
            return certificateService.Delete();
        }

        [HttpPost]
        [Route(nameof(Create))]
        [Authorize(AuthenticationSchemes="auth", Roles = "Admin")]
        public Task Create(string serverEndPoint, string subjectName)
        {
            return CertificateService.Create(serverEndPoint, subjectName);
        }
    }
}
