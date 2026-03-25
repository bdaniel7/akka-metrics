export interface NodeInfo {
  nodeId: string;
  hostname: string;
  remoteAddress: string;
  connectedAt: string;
}

export interface MetricUpdate {
  nodeId: string;
  hostname: string;
  cpuPercent: number;
  ramUsedMb: number;
  ramTotalMb: number;
  ramPercent: number;
  timestamp: string;
}

export interface CollectorStatus {
  nodeId: string;
  isCollecting: boolean;
  intervalMs: number;
  startedAt: string | null;
}

export interface NodeMetricsState {
  info: NodeInfo;
  latestMetric: MetricUpdate | null;
  isCollecting: boolean;
  history: MetricUpdate[];
}
