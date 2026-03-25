import { Component, Input, OnChanges, SimpleChanges } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { NodeMetricsState } from '../models/metrics.models';
import { CollectorApiService } from '../services/collector-api.service';
import { GaugeComponent } from './gauge.component';
import { SparklineComponent } from './sparkline.component';

@Component({
  selector: 'app-node-card',
  standalone: true,
  imports: [CommonModule, DecimalPipe, FormsModule, GaugeComponent, SparklineComponent],
  styleUrl: './node-card.component.less',
  template: `
    <div class="card fade-in" [class.live]="state.isCollecting">

      <!-- Top accent bar: colour driven by live state, no inline style needed for static colours -->
      <div class="top-bar"
           [style.background]="state.isCollecting
             ? 'linear-gradient(90deg, #22c55e, #16a34a)'
             : '#e2e8f0'">
      </div>

      <div class="card-body">

        <!-- Header -->
        <div class="card-header">
          <div class="card-header-left">
            <div class="hostname-row">
              <span class="status-dot status-pulse"
                    [style.background]="state.isCollecting ? '#22c55e' : '#cbd5e1'"></span>
              <span class="hostname" title="{{ state.info.hostname }}">{{ state.info.hostname }}</span>
            </div>
            <div class="nodeid">{{ state.info.nodeId }}</div>
          </div>
          <span class="badge"
                [class.badge-live]="state.isCollecting"
                [class.badge-idle]="!state.isCollecting">
            {{ state.isCollecting ? 'LIVE' : 'IDLE' }}
          </span>
        </div>

        <!-- Gauges -->
        <div class="gauges">
          <app-gauge [value]="state.latestMetric?.cpuPercent ?? 0" label="CPU"
                     [color]="cpuColor" [size]="108"></app-gauge>
          <app-gauge [value]="state.latestMetric?.ramPercent ?? 0" label="RAM"
                     color="#8b5cf6" [size]="108"></app-gauge>
        </div>

        <!-- RAM bar -->
        <div class="section">
          <div class="ram-row">
            <span class="ram-label">Memory usage</span>
            <span class="ram-value">
              {{ (state.latestMetric?.ramUsedMb ?? 0) | number:'1.0-0' }}
              / {{ (state.latestMetric?.ramTotalMb ?? 0) | number:'1.0-0' }} MB
            </span>
          </div>
          <div class="ram-track">
            <div class="ram-fill" [style.width.%]="state.latestMetric?.ramPercent ?? 0"></div>
          </div>
        </div>

        <!-- Sparklines -->
        <div class="sparkline-grid">
          <div class="section">
            <div class="section-label sparkline-label-cpu">CPU history</div>
            <app-sparkline [data]="cpuHistory" color="#8b5cf6"
                           fillColor="rgba(139,92,246,0.12)" [height]="44" [width]="120"></app-sparkline>
          </div>
          <div class="section">
            <div class="section-label sparkline-label-ram">RAM history</div>
            <app-sparkline [data]="ramHistory" color="#3b82f6"
                           fillColor="rgba(59,130,246,0.12)" [height]="44" [width]="120"></app-sparkline>
          </div>
        </div>

        <!-- Control panel -->
        <div class="section">
          <div class="section-label">Collector REST API URL</div>
          <input class="url-input" type="text" [(ngModel)]="collectorUrl"
                 [placeholder]="placeholderUrl" />
          <div class="ctrl-row">
            <span class="feedback feedback-ok" *ngIf="successMsg">✓ {{ successMsg }}</span>
            <span class="feedback feedback-err" *ngIf="errorMsg">✗ {{ errorMsg }}</span>
            <button class="btn"
                    [class.btn-go]="!state.isCollecting"
                    [class.btn-stop]="state.isCollecting"
                    (click)="toggle()" [disabled]="loading">
              <span class="spinner" *ngIf="loading"></span>
              <span *ngIf="!loading">{{ state.isCollecting ? '⏹ STOP' : '▶ START' }}</span>
            </button>
          </div>
        </div>

        <!-- Footer -->
        <div class="card-footer">
          <span class="footer-text footer-text-addr" title="{{ state.info.remoteAddress }}">
            {{ state.info.remoteAddress }}
          </span>
          <span class="footer-text" *ngIf="state.latestMetric">{{ ts(state.latestMetric.timestamp) }}</span>
        </div>
      </div>
    </div>
  `,
})
export class NodeCardComponent implements OnChanges {
  @Input() state!: NodeMetricsState;
  collectorUrl = '';
  loading      = false;
  errorMsg     = '';
  successMsg   = '';
  cpuHistory: number[] = [];
  ramHistory: number[] = [];
  private urlTouched = false;

  constructor(private api: CollectorApiService) {}

  ngOnChanges(_: SimpleChanges) {
    this.cpuHistory = this.state.history.map(m => m.cpuPercent);
    this.ramHistory = this.state.history.map(m => m.ramPercent);
    if (!this.urlTouched && !this.collectorUrl)
      this.collectorUrl = this.derive(this.state.info.remoteAddress);
  }

  get placeholderUrl() { return this.derive(this.state?.info?.remoteAddress ?? '') || 'http://hostname:5001'; }
  private derive(addr: string) { const m = addr.match(/@([^:]+):\d+/); return m ? `http://${m[1]}:5001` : ''; }

  get cpuColor() {
    const v = this.state.latestMetric?.cpuPercent ?? 0;
    return v > 80 ? '#ef4444' : v > 60 ? '#f97316' : '#8b5cf6';
  }

  toggle() {
    const url = this.collectorUrl || this.placeholderUrl;
    if (!url || url.includes('hostname')) {
      this.errorMsg = 'Enter the collector URL'; setTimeout(() => this.errorMsg = '', 3000); return;
    }
    this.loading = true; this.errorMsg = ''; this.successMsg = ''; this.urlTouched = true;
    (this.state.isCollecting ? this.api.stopCollecting(url) : this.api.startCollecting(url)).subscribe({
      next:  s => { this.loading = false; this.successMsg = s.isCollecting ? 'Started' : 'Stopped'; setTimeout(() => this.successMsg = '', 2500); },
      error: e => { this.loading = false; this.errorMsg = e?.message ?? 'Request failed'; setTimeout(() => this.errorMsg = '', 4000); },
    });
  }

  ts(t: string) { try { return new Date(t).toLocaleTimeString(); } catch { return ''; } }
}
