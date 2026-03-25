import { Component, Input, OnInit, OnDestroy } from '@angular/core';
import { CommonModule, DatePipe, UpperCasePipe } from '@angular/common';

@Component({
  selector: 'app-header',
  standalone: true,
  imports: [CommonModule, DatePipe, UpperCasePipe],
  styleUrl: './header.component.less',
  template: `
    <header>
      <div class="inner">

        <!-- Brand -->
        <div class="brand-icon-wrap">
          <div class="brand-icon">
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="white" stroke-width="2">
              <rect x="2" y="3" width="20" height="14" rx="2"/>
              <path d="M8 21h8M12 17v4"/>
            </svg>
            <span class="dot brand-live-dot status-pulse"></span>
          </div>
          <div>
            <div class="brand-name">SYSWATCH</div>
            <div class="brand-sub">DISTRIBUTED METRICS</div>
          </div>
        </div>

        <!-- Pills -->
        <div class="pills">
          <div class="pill">
            <span class="dot status-pulse" [style.background]="connColor"></span>
            <span class="pill-conn-label" [style.color]="connColor">{{ connectionState | uppercase }}</span>
          </div>
          <div class="pill">
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="icon-nodes">
              <rect x="2" y="2" width="9" height="9"/><rect x="13" y="2" width="9" height="9"/>
              <rect x="2" y="13" width="9" height="9"/><rect x="13" y="13" width="9" height="9"/>
            </svg>
            <span class="pill-node-count">{{ nodeCount }}</span>
            <span class="pill-nodes-label">nodes</span>
          </div>
          <div class="pill">
            <svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" class="icon-live">
              <polyline points="22 12 18 12 15 21 9 3 6 12 2 12"/>
            </svg>
            <span class="pill-live-count">{{ activeNodes }}</span>
            <span class="pill-live-label">live</span>
          </div>
        </div>

        <div class="clock">{{ now | date:'HH:mm:ss' }}</div>
      </div>
    </header>
  `,
})
export class HeaderComponent implements OnInit, OnDestroy {
  @Input() connectionState = 'disconnected';
  @Input() nodeCount   = 0;
  @Input() activeNodes = 0;
  now = new Date();
  private _t: any;
  ngOnInit()    { this._t = setInterval(() => this.now = new Date(), 1000); }
  ngOnDestroy() { clearInterval(this._t); }
  get connColor() {
    return this.connectionState === 'connected'  ? '#22c55e'
         : this.connectionState === 'connecting' ? '#f59e0b'
         : '#ef4444';
  }
}
