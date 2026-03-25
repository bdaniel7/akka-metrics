using Akka.Actor;
using AkkaMetrics.Hub.Actors;
using AkkaMetrics.Hub.Services;
using AkkaMetrics.Shared;
using Microsoft.AspNetCore.SignalR;

namespace AkkaMetrics.Hub.Hubs
{
    /// <summary>
    /// SignalR hub that bridges Angular clients with the Akka actor system.
    /// Clients connect here to receive real-time metric updates.
    /// </summary>
    public class MetricsSignalRHub : Microsoft.AspNetCore.SignalR.Hub
    {
        private readonly AkkaHubService akkaService;

        public MetricsSignalRHub(AkkaHubService akkaService)
        {
            this.akkaService = akkaService;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();

            // Send current node list to the newly connected client
            try
            {
                var timeout = TimeSpan.FromSeconds(5);
                var nodes = await akkaService.HubActor
                    .Ask<ConnectedNodes>(
                                         new GetConnectedNodes(), timeout);

                await Clients.Caller.SendAsync("NodeList", nodes.Nodes.Select(n => new
                {
                    nodeId = n.NodeId,
                    hostname = n.Hostname,
                    remoteAddress = n.RemoteAddress,
                    connectedAt = n.ConnectedAt
                }));

                // Send latest snapshots
                var snapshots = await akkaService.HubActor
                    .Ask<LatestSnapshots>(new GetLatestSnapshots(), timeout);

                foreach (var s in snapshots.Snapshots)
                {
                    await Clients.Caller.SendAsync("MetricUpdate", new
                    {
                        nodeId = s.NodeId,
                        hostname = s.Hostname,
                        cpuPercent = Math.Round(s.CpuPercent, 2),
                        ramUsedMb = Math.Round(s.RamUsedMb, 2),
                        ramTotalMb = Math.Round(s.RamTotalMb, 2),
                        ramPercent = Math.Round(s.RamPercent, 2),
                        timestamp = s.Timestamp.ToString("O")
                    });
                }
            }
            catch (Exception ex)
            {
                // Log but don't crash - actors may not be ready yet
                Console.Error.WriteLine($"Error sending initial state: {ex.Message}");
            }
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            return base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Clients can call this to request the current node list.
        /// </summary>
        public async Task RequestNodeList()
        {
            var nodes = await akkaService.HubActor
                .Ask<ConnectedNodes>(
                                     new GetConnectedNodes(),
                                     TimeSpan.FromSeconds(5));

            await Clients.Caller.SendAsync("NodeList", nodes.Nodes.Select(n => new
            {
                nodeId = n.NodeId,
                hostname = n.Hostname,
                remoteAddress = n.RemoteAddress,
                connectedAt = n.ConnectedAt
            }));
        }
    }
}
