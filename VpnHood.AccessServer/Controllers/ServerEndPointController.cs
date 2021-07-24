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
    public class ServerEndPointController : SuperController<ServerEndPointController>
    {

        public ServerEndPointController(ILogger<ServerEndPointController> logger) : base(logger)
        {
        }

        [HttpPost]
        [Route(nameof(Delete))]
        [Authorize(AuthenticationSchemes="auth", Roles = "Admin")]
        public Task Delete(string serverEndPoint)
        {
            var certificateService = ServerEndPointService.FromId(serverEndPoint);
            return certificateService.Delete();
        }

        [HttpPost]
        [Route(nameof(CreateFromCertificate))]
        [Authorize(AuthenticationSchemes = "auth", Roles = "Admin")]
        public Task CreateFromCertificate(string serverEndPoint, int serverEndPointGroupId, byte[] certificateRawData, string password = null, bool isDefault = false, bool overwrite = false)
        {
            return ServerEndPointService.Create(serverEndPoint: serverEndPoint, rawData: certificateRawData, password: password,
                overwrite: overwrite, serverEndPointGroupId: serverEndPointGroupId);
        }

        [HttpPost]
        [Route(nameof(Create))]
        [Authorize(AuthenticationSchemes="auth", Roles = "Admin")]
        public Task Create(string serverEndPoint, string subjectName, int serverEndPointGroupId, bool isDefault = false)
        {
            return ServerEndPointService.Create(serverEndPoint, subjectName, serverEndPointGroupId: serverEndPointGroupId);
        }
    }
}
