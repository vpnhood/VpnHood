using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace VpnHood.AccessServer.Controllers
{
    [Route("/api/projects/{projectId:guid}/users")]
    public class UserController : SuperController<UserController>
    {
        public UserController(ILogger<UserController> logger) : base(logger)
        {
        }
    }
}