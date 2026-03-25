import { Injectable, OnDestroy } from '@angular/core';
import { BehaviorSubject, Subscription } from 'rxjs';
import { MetricsSignalRService } from './metrics-signalr.service';
import { NodeInfo, MetricUpdate, NodeMetricsState } from '../models/metrics.models';

const MAX_HISTORY = 60; // Keep 60 data points per node

@Injectable({ providedIn: 'root' })
export class MetricsStoreService implements OnDestroy {
  private readonly nodesMap = new Map<string, NodeMetricsState>();
  readonly nodes$ = new BehaviorSubject<NodeMetricsState[]>([]);
  
  private subs = new Subscription();

  constructor(private signalR: MetricsSignalRService) {
    this.subs.add(
      this.signalR.nodeList$.subscribe(nodes => this.handleNodeList(nodes))
    );
    this.subs.add(
      this.signalR.metricUpdate$.subscribe(update => this.handleMetricUpdate(update))
    );
    this.subs.add(
      this.signalR.nodeConnected$.subscribe(node => this.handleNodeConnected(node))
    );
    this.subs.add(
      this.signalR.nodeDisconnected$.subscribe(nodeId => this.handleNodeDisconnected(nodeId))
    );
  }

  private handleNodeList(nodes: NodeInfo[]): void {
    // Add new nodes, keep existing history
    nodes.forEach(node => {
      if (!this.nodesMap.has(node.nodeId)) {
        this.nodesMap.set(node.nodeId, {
          info: node,
          latestMetric: null,
          isCollecting: false,
          history: []
        });
      } else {
        const existing = this.nodesMap.get(node.nodeId)!;
        this.nodesMap.set(node.nodeId, { ...existing, info: node });
      }
    });

    // Remove nodes no longer in list
    const nodeIds = new Set(nodes.map(n => n.nodeId));
    for (const key of this.nodesMap.keys()) {
      if (!nodeIds.has(key)) this.nodesMap.delete(key);
    }

    this.emit();
  }

  private handleMetricUpdate(update: MetricUpdate): void {
    const state = this.nodesMap.get(update.nodeId);
    if (!state) {
      // Node appeared before registration (race condition) - create entry
      this.nodesMap.set(update.nodeId, {
        info: { nodeId: update.nodeId, hostname: update.hostname, remoteAddress: '', connectedAt: new Date().toISOString() },
        latestMetric: update,
        isCollecting: true,
        history: [update]
      });
    } else {
      const history = [...state.history, update].slice(-MAX_HISTORY);
      this.nodesMap.set(update.nodeId, {
        ...state,
        latestMetric: update,
        isCollecting: true,
        history
      });
    }
    this.emit();
  }

  private handleNodeConnected(node: NodeInfo): void {
    if (!this.nodesMap.has(node.nodeId)) {
      this.nodesMap.set(node.nodeId, {
        info: node, latestMetric: null, isCollecting: false, history: []
      });
      this.emit();
    }
  }

  private handleNodeDisconnected(nodeId: string): void {
    this.nodesMap.delete(nodeId);
    this.emit();
  }

  private emit(): void {
    this.nodes$.next(Array.from(this.nodesMap.values()));
  }

  ngOnDestroy(): void {
    this.subs.unsubscribe();
  }
}
