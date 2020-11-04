using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace VpnHood.AccessServer.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public partial class AccessController : ControllerBase, IAccessServer
    {

        private readonly ILogger<AccessController> _logger;

        public AccessController(ILogger<AccessController> logger)
        {
            _logger = logger;
        }


        [HttpPost]
        [Route("addusage")]
        public Task<Access> AddUsage(AddUsageParams addUsageParams)
        {
           // _logger.LogInformation($"AddUsage for {addUsageParam.ClientIdentity}, SentTraffic: {addUsageParam.SentTrafficByteCount / 1000000} MB, ReceivedTraffic: {addUsageParam.ReceivedTrafficByteCount / 1000000} MB");

            var access = new Access()
            {
                AccessId = "fasfsaf"
            };

            return Task.FromResult(access);
        }


        [HttpGet]
        public Task<Access> GetAccess(ClientIdentity clientIdentity)
        {
            var access = new Access()
            {
                AccessId = "fasfsaf"
            };

            return Task.FromResult(access);
        }
    
    }
}
