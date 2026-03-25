import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { HttpClientModule } from '@angular/common/http';
import { Subscription } from 'rxjs';
import { MetricsSignalRService } from './services/metrics-signalr.service';
import { MetricsStoreService } from './services/metrics-store.service';
import { NodeMetricsState } from './models/metrics.models';
import { HeaderComponent } from './components/header.component';
import { NodeCardComponent } from './components/node-card.component';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, DecimalPipe, HttpClientModule, HeaderComponent, NodeCardComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  styleUrl: './app.component.less',
  template: `
    <div class="page">
      <app-header [connectionState]="connectionState"
                  [nodeCount]="nodes.length"
                  [activeNodes]="activeCount"></app-header>

      <div class="main">

        <!-- ── Empty state ── -->
        <div class="empty" *ngIf="nodes.length === 0">
          <div class="empty-icon-pos">
            <div class="empty-icon-ring">
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="#94a3b8" stroke-width="1.5">
                <rect x="2" y="2" width="9" height="9"/><rect x="13" y="2" width="9" height="9"/>
                <rect x="2" y="13" width="9" height="9"/><rect x="13" y="13" width="9" height="9"/>
              </svg>
            </div>
            <div class="empty-pulse-ring"></div>
          </div>
          <div class="empty-text-center">
            <div class="empty-title">No collectors connected</div>
            <div class="empty-subtitle">Deploy MetricsCollector on any machine to see live data here</div>
          </div>
          <div class="empty-box">
            <div class="qs-title">Quick Start</div>
            <div class="qs-steps">
              <div>1. Start <code>MetricsHub</code> — this service</div>
              <div>2. Set Hub address in collector config:</div>
              <div class="qs-codebox">"HubAddress": "akka.tcp://MetricsHub&#64;&lt;hub-ip&gt;:8080/user/metrics-hub"</div>
              <div>3. Run <code>MetricsCollector</code> on each machine</div>
              <div>4. Enter the URL in a card and click <strong class="qs-cta">▶ START</strong></div>
            </div>
          </div>
        </div>

        <!-- ── Stats bar ── -->
        <div class="stats-grid" *ngIf="nodes.length > 0">
          <div class="stat">
            <div class="stat-label">Total Nodes</div>
            <div class="stat-value">{{ nodes.length }}</div>
          </div>
          <div class="stat">
            <div class="stat-label">Live Collecting</div>
            <div class="stat-value stat-value-accent">{{ activeCount }}</div>
          </div>
          <div class="stat">
            <div class="stat-label">Avg CPU</div>
            <div class="stat-value" [style.color]="cpuColor(avgCpu)">{{ avgCpu | number:'1.1-1' }}%</div>
          </div>
          <div class="stat">
            <div class="stat-label">Avg RAM</div>
            <div class="stat-value stat-value-ram">{{ avgRam | number:'1.1-1' }}%</div>
          </div>
        </div>

        <!-- ── Node grid ── -->
        <div class="node-grid" *ngIf="nodes.length > 0">
          <app-node-card *ngFor="let n of nodes; trackBy:track" [state]="n"></app-node-card>
        </div>

      </div>

      <footer class="app-footer">
        <span class="footer-text">SYSWATCH · Akka.NET Remoting + Streams · .NET 8 · Angular · SignalR</span>
      </footer>
    </div>
  `,
})
export class AppComponent implements OnInit, OnDestroy {
  nodes: NodeMetricsState[] = [];
  connectionState = 'disconnected';
  private subs = new Subscription();

  constructor(
    private signalR: MetricsSignalRService,
    private store:   MetricsStoreService,
    private cdr:     ChangeDetectorRef,
  ) {}

  ngOnInit() {
    this.signalR.connect();
    this.subs.add(this.signalR.connectionState$.subscribe(s => { this.connectionState = s; this.cdr.markForCheck(); }));
    this.subs.add(this.store.nodes$.subscribe(n => { this.nodes = n; this.cdr.markForCheck(); }));
  }

  get activeCount() { return this.nodes.filter(n => n.isCollecting).length; }
  get avgCpu()  {
    const a = this.nodes.filter(n => n.latestMetric);
    return a.length ? a.reduce((s,n) => s+(n.latestMetric?.cpuPercent??0),0)/a.length : 0;
  }
  get avgRam()  {
    const a = this.nodes.filter(n => n.latestMetric);
    return a.length ? a.reduce((s,n) => s+(n.latestMetric?.ramPercent??0),0)/a.length : 0; }
  cpuColor(v: number) { return v > 80 ? '#ef4444' : v > 60 ? '#f97316' : '#8b5cf6'; }
  track(_: number, n: NodeMetricsState) { return n.info.nodeId; }
  ngOnDestroy() { this.subs.unsubscribe(); this.signalR.disconnect(); }
}
