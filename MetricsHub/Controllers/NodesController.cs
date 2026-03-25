using Akka.Actor;
using AkkaMetrics.Hub.Services;
using AkkaMetrics.Shared;
using Microsoft.AspNetCore.Mvc;

namespace AkkaMetrics.Hub.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class NodesController : ControllerBase
    {
        private readonly AkkaHubService akkaService;

        public NodesController(AkkaHubService akkaService)
        {
            this.akkaService = akkaService;
        }

        /// <summary>
        /// Get all currently connected collector nodes.
        /// </summary>
        [HttpGet]
        [ProducesResponseType(typeof(IEnumerable<object>), 200)]
        public async Task<IActionResult> GetNodes()
        {
            var result = await akkaService.HubActor
                .Ask<ConnectedNodes>(new GetConnectedNodes(), TimeSpan.FromSeconds(5));

            return Ok(result.Nodes.Select(n => new
            {
                nodeId = n.NodeId,
                hostname = n.Hostname,
                remoteAddress = n.RemoteAddress,
                connectedAt = n.ConnectedAt
            }));
        }
    }
}
