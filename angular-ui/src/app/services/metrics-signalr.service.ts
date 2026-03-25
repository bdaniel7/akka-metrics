import { Injectable, OnDestroy } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { BehaviorSubject, Subject } from 'rxjs';
import { NodeInfo, MetricUpdate } from '../models/metrics.models';

@Injectable({ providedIn: 'root' })
export class MetricsSignalRService implements OnDestroy {
  private hubConnection: signalR.HubConnection | null = null;

  // Observables
  readonly connectionState$ = new BehaviorSubject<'connecting' | 'connected' | 'disconnected' | 'error'>('disconnected');
  readonly nodeList$ = new BehaviorSubject<NodeInfo[]>([]);
  readonly metricUpdate$ = new Subject<MetricUpdate>();
  readonly nodeConnected$ = new Subject<NodeInfo>();
  readonly nodeDisconnected$ = new Subject<string>();

  private readonly HUB_URL = '/hubs/metrics';

  connect(): void {
    if (this.hubConnection) return;

    this.connectionState$.next('connecting');

    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(this.HUB_URL)
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          const delays = [1000, 2000, 5000, 10000];
          return delays[Math.min(retryContext.previousRetryCount, delays.length - 1)];
        }
      })
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    this.registerHandlers();

    this.hubConnection.start()
      .then(() => {
        this.connectionState$.next('connected');
        console.log('[SignalR] Connected to MetricsHub');
      })
      .catch(err => {
        this.connectionState$.next('error');
        console.error('[SignalR] Connection error:', err);
        // Retry after 5s
        setTimeout(() => {
          this.hubConnection = null;
          this.connect();
        }, 5000);
      });

    this.hubConnection.onreconnecting(() => this.connectionState$.next('connecting'));
    this.hubConnection.onreconnected(() => this.connectionState$.next('connected'));
    this.hubConnection.onclose(() => this.connectionState$.next('disconnected'));
  }

  disconnect(): void {
    this.hubConnection?.stop();
    this.hubConnection = null;
    this.connectionState$.next('disconnected');
  }

  private registerHandlers(): void {
    if (!this.hubConnection) return;

    this.hubConnection.on('NodeList', (nodes: NodeInfo[]) => {
      this.nodeList$.next(nodes);
    });

    this.hubConnection.on('MetricUpdate', (update: MetricUpdate) => {
      this.metricUpdate$.next(update);
    });

    this.hubConnection.on('NodeConnected', (node: NodeInfo) => {
      this.nodeConnected$.next(node);
      // Update node list
      const current = this.nodeList$.value;
      if (!current.find(n => n.nodeId === node.nodeId)) {
        this.nodeList$.next([...current, node]);
      }
    });

    this.hubConnection.on('NodeDisconnected', (payload: { nodeId: string }) => {
      this.nodeDisconnected$.next(payload.nodeId);
      this.nodeList$.next(this.nodeList$.value.filter(n => n.nodeId !== payload.nodeId));
    });
  }

  ngOnDestroy(): void {
    this.disconnect();
  }
}
