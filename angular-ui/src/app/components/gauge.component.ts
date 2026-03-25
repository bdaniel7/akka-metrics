import { Component, Input, OnChanges } from '@angular/core';
import { CommonModule, DecimalPipe } from '@angular/common';

@Component({
  selector: 'app-gauge',
  standalone: true,
  imports: [CommonModule, DecimalPipe],
  styleUrl: './gauge.component.less',
  template: `
    <div class="gauge-wrap">
      <div class="gauge-svg-wrap" [style.width.px]="size" [style.height.px]="size">
        <svg class="gauge-svg"
             [attr.width]="size" [attr.height]="size" [attr.viewBox]="'0 0 '+size+' '+size">
          <!-- Track arc -->
          <circle [attr.cx]="c" [attr.cy]="c" [attr.r]="r"
                  fill="none" stroke="#e2e8f0" [attr.stroke-width]="sw"
                  [attr.stroke-dasharray]="trackLen+' '+circ"
                  stroke-linecap="round"/>
          <!-- Value arc -->
          <circle [attr.cx]="c" [attr.cy]="c" [attr.r]="r"
                  fill="none" [attr.stroke]="color" [attr.stroke-width]="sw"
                  [attr.stroke-dasharray]="trackLen+' '+circ"
                  [attr.stroke-dashoffset]="offset"
                  stroke-linecap="round"
                  style="transition:stroke-dashoffset 0.6s cubic-bezier(.4,0,.2,1)"
                  [attr.filter]="'drop-shadow(0 0 4px '+color+'66)'"/>
        </svg>
        <div class="gauge-center">
          <span class="gauge-value" [style.font-size.px]="size * 0.2">
            {{ value | number:'1.0-0' }}<span class="gauge-pct" [style.font-size.px]="size * 0.12"> %</span>
          </span>
        </div>
      </div>
      <span class="gauge-label" [style.color]="color">{{ label }}</span>
    </div>
  `,
})
export class GaugeComponent implements OnChanges {
  @Input() value = 0;
  @Input() label = '';
  @Input() color = '#3b82f6';
  @Input() size  = 100;

  sw = 8; r = 0; c = 0; circ = 0; trackLen = 0; offset = 0;

  ngOnChanges() {
    this.sw       = this.size * 0.08;
    this.r        = this.size / 2 - this.sw;
    this.c        = this.size / 2;
    this.circ     = 2 * Math.PI * this.r;
    this.trackLen = this.circ * 0.75;
    const pct     = Math.max(0, Math.min(100, this.value));
    this.offset   = this.trackLen - (this.trackLen * pct / 100);
  }
}
