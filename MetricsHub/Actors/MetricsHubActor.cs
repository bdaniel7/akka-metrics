using System.Diagnostics;
using Akka.Actor;
using Akka.Event;
using AkkaMetrics.Hub.Hubs;
using AkkaMetrics.Shared;
using Microsoft.AspNetCore.SignalR;

namespace AkkaMetrics.Hub.Actors
{
    /// <summary>
    /// Central hub actor. Receives metrics from remote collectors and
    /// broadcasts them to SignalR clients.
    /// </summary>
    public class MetricsHubActor : ReceiveActor
    {
        private readonly ILoggingAdapter log = Context.GetLogger();
        private readonly IHubContext<MetricsSignalRHub> signalRHub;
        private static readonly ActivitySource ActivitySource = new("AkkaMetrics.Hub");

        // Track connected collector nodes
        private readonly Dictionary<string, NodeInfo> connectedNodes = new();
        // Latest snapshot per node for newly connected UI clients
        private readonly Dictionary<string, MetricSnapshot> latestSnapshots = new();

        public MetricsHubActor(IHubContext<MetricsSignalRHub> signalRHub)
        {
            this.signalRHub = signalRHub;

            Receive<RegisterCollector>(msg => handleRegister(msg));
            Receive<UnregisterCollector>(msg => handleUnregister(msg));
            Receive<PushMetrics>(msg => handlePushMetrics(msg));
            Receive<GetConnectedNodes>(_ => handleGetNodes());
            Receive<GetLatestSnapshots>(_ => handleGetLatestSnapshots());
            Receive<Terminated>(msg => handleTerminated(msg));
        }

        private void handleRegister(RegisterCollector msg)
        {
            using var activity = ActivitySource.StartActivity("RegisterCollector", ActivityKind.Server);
            var remoteAddress = Sender.Path.Address.ToString();
            activity?.SetTag("node.id", msg.NodeId);
            activity?.SetTag("hostname", msg.Hostname);
            activity?.SetTag("remote.address", remoteAddress);
            log.Info("Node '{NodeId}' ({Hostname}) connected from {Remote}", 
                msg.NodeId, msg.Hostname, remoteAddress);

            // Watch the remote actor for death watch
            Context.Watch(Sender);

            var nodeInfo = new NodeInfo(
                msg.NodeId,
                msg.Hostname,
                remoteAddress,
                DateTimeOffset.UtcNow
            );
            connectedNodes[msg.NodeId] = nodeInfo;

            // Notify all UI clients of new node
            _ = signalRHub.Clients.All.SendAsync("NodeConnected", new
            {
                nodeId = nodeInfo.NodeId,
                hostname = nodeInfo.Hostname,
                remoteAddress = nodeInfo.RemoteAddress,
                connectedAt = nodeInfo.ConnectedAt
            });

            _ = broadcastNodeList();
            activity?.SetTag("total.nodes", connectedNodes.Count);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        private void handleUnregister(UnregisterCollector msg)
        {
            if (connectedNodes.Remove(msg.NodeId, out var node))
            {
                log.Info("Node '{NodeId}' unregistered", msg.NodeId);
                latestSnapshots.Remove(msg.NodeId);

                _ = signalRHub.Clients.All.SendAsync("NodeDisconnected", new
                {
                    nodeId = msg.NodeId
                });

                _ = broadcastNodeList();
            }
        }

        private void handlePushMetrics(PushMetrics msg)
        {
            using var activity = ActivitySource.StartActivity("ProcessMetrics", ActivityKind.Internal);
            var snapshot = msg.Snapshot;
            
            activity?.SetTag("node.id", snapshot.NodeId);
            activity?.SetTag("cpu.percent", snapshot.CpuPercent);
            activity?.SetTag("ram.percent", snapshot.RamPercent);
            activity?.SetTag("connected.nodes", connectedNodes.Count);
            latestSnapshots[snapshot.NodeId] = snapshot;

            log.Debug("[{NodeId}] CPU={Cpu:F1}% RAM={Ram:F0}MB/{Total:F0}MB",
                snapshot.NodeId, snapshot.CpuPercent, snapshot.RamUsedMb, snapshot.RamTotalMb);

            // Push to all SignalR clients
            _ = signalRHub.Clients.All.SendAsync("MetricUpdate", new
            {
                nodeId = snapshot.NodeId,
                hostname = snapshot.Hostname,
                cpuPercent = Math.Round(snapshot.CpuPercent, 2),
                ramUsedMb = Math.Round(snapshot.RamUsedMb, 2),
                ramTotalMb = Math.Round(snapshot.RamTotalMb, 2),
                ramPercent = Math.Round(snapshot.RamPercent, 2),
                timestamp = snapshot.Timestamp.ToString("O")
            });
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        private void handleGetNodes()
        {
            Sender.Tell(new ConnectedNodes(connectedNodes.Values.ToList()));
        }

        private void handleGetLatestSnapshots()
        {
            Sender.Tell(new LatestSnapshots(latestSnapshots.Values.ToList()));
        }

        private void handleTerminated(Terminated msg)
        {
            // Find and remove the node whose actor died
            var nodeId = connectedNodes
                .Where(kv => kv.Value.RemoteAddress.Contains(msg.ActorRef.Path.Address.Host ?? ""))
                .Select(kv => kv.Key)
                .FirstOrDefault();

            if (nodeId != null)
            {
                log.Warning("Remote actor for node '{NodeId}' terminated unexpectedly", nodeId);
                connectedNodes.Remove(nodeId);
                latestSnapshots.Remove(nodeId);

                _ = signalRHub.Clients.All.SendAsync("NodeDisconnected", new { nodeId });
                _ = broadcastNodeList();
            }
        }

        private async Task broadcastNodeList()
        {
            await signalRHub.Clients.All.SendAsync("NodeList", connectedNodes.Values.Select(n => new
            {
                nodeId = n.NodeId,
                hostname = n.Hostname,
                remoteAddress = n.RemoteAddress,
                connectedAt = n.ConnectedAt
            }));
        }
    }

    // Additional inner message
    public record GetLatestSnapshots();
    public record LatestSnapshots(List<MetricSnapshot> Snapshots);
}
