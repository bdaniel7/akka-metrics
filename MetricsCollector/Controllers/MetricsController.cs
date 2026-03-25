using Akka.Actor;
using AkkaMetrics.Collector.Services;
using AkkaMetrics.Shared;
using Microsoft.AspNetCore.Mvc;

namespace AkkaMetrics.Collector.Controllers
{
    /// <summary>
    /// REST API to start/stop metric collection on this node.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class MetricsController : ControllerBase
    {
        readonly AkkaService akkaService;
        readonly ILogger<MetricsController> logger;
        static readonly TimeSpan askTimeout = TimeSpan.FromSeconds(5);

        public MetricsController(AkkaService akkaService, ILogger<MetricsController> logger)
        {
            this.akkaService = akkaService;
            this.logger = logger;
        }

        /// <summary>
        /// Get current collector status.
        /// </summary>
        [HttpGet("status")]
        [ProducesResponseType(typeof(CollectorStatusDto), 200)]
        public async Task<IActionResult> GetStatus()
        {
            try
            {
                var status = await akkaService.CollectorActor
                    .Ask<CollectorStatus>(new GetCollectorStatus(), askTimeout);
                return Ok(mapToDto(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to get collector status");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Start collecting metrics.
        /// </summary>
        /// <param name="request">Optional configuration for collection interval.</param>
        [HttpPost("start")]
        [ProducesResponseType(typeof(CollectorStatusDto), 200)]
        public async Task<IActionResult> Start([FromBody] StartRequest? request = null)
        {
            try
            {
                var intervalMs = request?.IntervalMs ?? 2000;
                var status = await akkaService.CollectorActor
                    .Ask<CollectorStatus>(new StartCollecting(intervalMs), askTimeout);
                return Ok(mapToDto(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to start collection");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Stop collecting metrics.
        /// </summary>
        [HttpPost("stop")]
        [ProducesResponseType(typeof(CollectorStatusDto), 200)]
        public async Task<IActionResult> Stop()
        {
            try
            {
                var status = await akkaService.CollectorActor
                    .Ask<CollectorStatus>(new StopCollecting(), askTimeout);
                return Ok(mapToDto(status));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to stop collection");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        static CollectorStatusDto mapToDto(CollectorStatus s) => new(
                                                                     s.NodeId,
                                                                     s.IsCollecting,
                                                                     s.IntervalMs,
                                                                     s.StartedAt?.ToString("O")
                                                                    );
    }

    public record StartRequest(int IntervalMs = 2000);
    public record CollectorStatusDto(string NodeId, bool IsCollecting, int IntervalMs, string? StartedAt);
}
