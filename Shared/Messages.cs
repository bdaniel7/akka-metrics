namespace AkkaMetrics.Shared
{
    // ── Metrics Data ─────────────────────────────────────────────────────────────

    [Serializable]
    public record MetricSnapshot(
        string NodeId,
        string Hostname,
        double CpuPercent,
        double RamUsedMb,
        double RamTotalMb,
        double RamPercent,
        DateTimeOffset Timestamp
    );

    // ── Collector → Hub Commands ──────────────────────────────────────────────────

    [Serializable]
    public record RegisterCollector(string NodeId, string Hostname);

    [Serializable]
    public record UnregisterCollector(string NodeId);

    [Serializable]
    public record PushMetrics(MetricSnapshot Snapshot);

    // ── REST API → Collector Actor Commands ──────────────────────────────────────

    [Serializable]
    public record StartCollecting(int IntervalMs = 2000);

    [Serializable]
    public record StopCollecting();

    [Serializable]
    public record GetCollectorStatus();

    [Serializable]
    public record CollectorStatus(string NodeId, bool IsCollecting, int IntervalMs, DateTimeOffset? StartedAt);

    // ── Hub → Clients ─────────────────────────────────────────────────────────────

    [Serializable]
    public record GetConnectedNodes();

    [Serializable]
    public record ConnectedNodes(List<NodeInfo> Nodes);

    [Serializable]
    public record NodeInfo(string NodeId, string Hostname, string RemoteAddress, DateTimeOffset ConnectedAt);
}
