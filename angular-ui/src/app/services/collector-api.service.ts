import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { CollectorStatus } from '../models/metrics.models';

@Injectable({ providedIn: 'root' })
export class CollectorApiService {
  constructor(private http: HttpClient) {}

  /**
   * Call a collector node's REST API to start collection.
   * collectorBaseUrl: e.g. "http://node1:5001"
   */
  startCollecting(collectorBaseUrl: string, intervalMs = 2000): Observable<CollectorStatus> {
    return this.http.post<CollectorStatus>(
      `${collectorBaseUrl}/api/metrics/start`,
      { intervalMs }
    );
  }

  stopCollecting(collectorBaseUrl: string): Observable<CollectorStatus> {
    return this.http.post<CollectorStatus>(
      `${collectorBaseUrl}/api/metrics/stop`,
      {}
    );
  }

  getStatus(collectorBaseUrl: string): Observable<CollectorStatus> {
    return this.http.get<CollectorStatus>(
      `${collectorBaseUrl}/api/metrics/status`
    );
  }
}
