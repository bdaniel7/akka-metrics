import { Component, Input, OnChanges, ElementRef, ViewChild, AfterViewInit } from '@angular/core';

@Component({
  selector: 'app-sparkline',
  standalone: true,
  template: `<canvas #c [attr.width]="width" [attr.height]="height" style="width:100%;display:block;">
  </canvas>`,
})
export class SparklineComponent implements OnChanges, AfterViewInit {
  @Input() data: number[]  = [];
  @Input() color           = '@kolor'; //'#3b82f6';
  @Input() fillColor       = 'rgba(59,130,246,0.15)';
  @Input() width           = 200;
  @Input() height          = 50;
  @Input() maxValue        = 100;
  @ViewChild('c') ref!: ElementRef<HTMLCanvasElement>;
  private ready = false;
  ngAfterViewInit() { this.ready = true; this.draw(); }
  ngOnChanges()     { if (this.ready) this.draw(); }

  private draw() {
    const canvas = this.ref?.nativeElement;
    if (!canvas) return;
    const ctx = canvas.getContext('2d')!;
    const w = this.width, h = this.height;
    const d = this.data.length >= 2 ? this.data : [0, 0];
    ctx.clearRect(0, 0, w, h);
    const step = w / (d.length - 1);
    const sy   = (v: number) => h - (Math.min(v, this.maxValue) / this.maxValue) * h * 0.85 - h * 0.08;
    // fill
    const grad = ctx.createLinearGradient(0, 0, 0, h);
    grad.addColorStop(0, this.fillColor);
    grad.addColorStop(1, 'rgba(255,255,255,0)');
    ctx.beginPath();
    ctx.moveTo(0, sy(d[0]));
    for (let i = 1; i < d.length; i++) {
      const px = (i-1)*step, py = sy(d[i-1]), x = i*step, y = sy(d[i]);
      ctx.bezierCurveTo((px+x)/2, py, (px+x)/2, y, x, y);
    }
    ctx.lineTo(w, h); ctx.lineTo(0, h); ctx.closePath();
    ctx.fillStyle = grad; ctx.fill();
    // line
    ctx.beginPath();
    ctx.moveTo(0, sy(d[0]));
    for (let i = 1; i < d.length; i++) {
      const px = (i-1)*step, py = sy(d[i-1]), x = i*step, y = sy(d[i]);
      ctx.bezierCurveTo((px+x)/2, py, (px+x)/2, y, x, y);
    }
    ctx.strokeStyle = this.color; ctx.lineWidth = 2;
    ctx.shadowBlur = 3; ctx.shadowColor = this.color; ctx.stroke(); ctx.shadowBlur = 0;
    // dot
    const lx = (d.length-1)*step, ly = sy(d[d.length-1]);
    ctx.beginPath(); ctx.arc(lx, ly, 3, 0, Math.PI*2);
    ctx.fillStyle = this.color; ctx.shadowBlur = 6; ctx.shadowColor = this.color;
    ctx.fill(); ctx.shadowBlur = 0;
  }
}
