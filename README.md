# SYSWATCH — Distributed Metrics Dashboard

A distributed system monitoring solution using **Akka.NET Remoting + Streams**, **ASP.NET Core 8**, **SignalR**, and **Angular** with Tailwind CSS.

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         Architecture Overview                           │
├─────────────────┐    Akka Remote TCP    ┌────────────────────────────┐  │
│  Machine A      │ ──────────────────►  │  MetricsHub                │  │
│  Collector      │  PushMetrics msg     │  (Central Akka Actor)      │  │
│  (Akka Actor +  │ ◄────────────────── │  Receives from all nodes   │  │
│   Streams tick) │  RegisterCollector   │  ──────────────────────►   │  │
│  REST API       │                      │  SignalR push to UI        │  │
└─────────────────┘                      └────────────────────────────┘  │
                                                       │                  │
┌─────────────────┐    Akka Remote TCP                 │  WebSocket      │
│  Machine B      │ ──────────────────►                ▼                 │
│  Collector      │                      ┌────────────────────────────┐  │
│                 │                      │  Angular UI                │  │
│  REST API       │ ◄───── HTTP ──────── │  SignalR Client            │  │
│  (start/stop)   │                      │  Tailwind dashboard        │  │
└─────────────────┘                      └────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

## Projects

| Project          | Description                                         | Ports           |
|------------------|-----------------------------------------------------|-----------------|
| `Shared`         | Shared message types for Akka remoting             | —               |
| `MetricsHub`     | Central hub: receives metrics, pushes via SignalR   | HTTP: 5000, Akka: 9080 |
| `MetricsCollector` | Per-machine collector with REST API              | HTTP: 5001, Akka: 8081 |
| `angular-ui`     | Real-time dashboard in Angular + Tailwind           | Dev: 4200       |

## Quick Start

### Option A — Docker Compose (recommended)

```bash
# From the repo root
docker-compose up --build
```

- UI: http://localhost
- Hub API + Swagger: http://localhost:5000/swagger
- Collector 1 API: http://localhost:5001
- Collector 2 API: http://localhost:5002

Start collection on node 1:
```bash
curl -X POST http://localhost:5001/api/metrics/start -H "Content-Type: application/json" -d '{"intervalMs": 2000}'
```

### Option B — Manual Run

**1. Start MetricsHub**
```bash
cd MetricsHub
dotnet run
# Listening on http://localhost:5000
# Akka TCP on port 9080
```

**2. Start a Collector** (on any machine)
```bash
cd MetricsCollector
# Edit appsettings.json: set HubAddress to the hub's address
dotnet run
# REST API on http://localhost:5001
```

**3. Start the Angular UI**
```bash
cd angular-ui
npm install
npm start
# Open http://localhost:4200
```

**4. Start collecting metrics via REST API**
```bash
# Start collection (default: every 2 seconds)
curl -X POST http://localhost:5001/api/metrics/start

# With custom interval
curl -X POST http://localhost:5001/api/metrics/start -H "Content-Type: application/json" -d '{"intervalMs": 1000}'

# Get status
curl http://localhost:5001/api/metrics/status

# Stop collection
curl -X POST http://localhost:5001/api/metrics/stop
```

## Deploying Collectors to Different Machines

1. Build the collector: `dotnet publish MetricsCollector -c Release`
2. Copy to target machine
3. Set environment variables or edit `appsettings.json`:
   ```json
   {
     "Akka": {
       "NodeId": "machine-name-or-uuid",
       "Port": "8081",
       "PublicHostname": "192.168.1.X",
       "HubAddress": "akka.tcp://MetricsHub@<HUB_IP>:9080/user/metrics-hub"
     }
   }
   ```
4. Run: `dotnet MetricsCollector.dll`
5. Start collection: `POST http://<collector-ip>:5001/api/metrics/start`

**Environment variable override example:**
```bash
Akka__NodeId=prod-server-1 \
Akka__PublicHostname=10.0.1.50 \
Akka__HubAddress="akka.tcp://MetricsHub@10.0.1.100:9080/user/metrics-hub" \
dotnet MetricsCollector.dll
```

## Architecture Details

### Akka.NET Remoting
- `MetricsHub` starts an `ActorSystem` named `MetricsHub` listening on TCP port 9080
- Each `MetricsCollector` creates an `ActorSystem` named `MetricsCollector`
- Collectors resolve the hub actor via: `akka.tcp://MetricsHub@<host>:9080/user/metrics-hub`
- Messages are serialized with `NewtonSoftJsonSerializer`

### Akka.Streams in the Collector
```csharp
Source.Tick(interval, interval, "tick")
    .ViaMaterialized(KillSwitches.Single<string>(), Keep.Right)
    .Select(_ => new CollectTick())
    .ToMaterialized(Sink.ActorRef<CollectTick>(self, new StreamCompleted()), Keep.Both)
    .Run(materializer);
```

The stream ticks at the configured interval, sends messages to the actor itself, which then collects metrics and pushes to the hub.

### SignalR Events (Hub → Angular)

| Event              | Payload                                      |
|--------------------|----------------------------------------------|
| `MetricUpdate`     | `{nodeId, hostname, cpuPercent, ramPercent, ramUsedMb, ramTotalMb, timestamp}` |
| `NodeList`         | `[{nodeId, hostname, remoteAddress, connectedAt}]` |
| `NodeConnected`    | `{nodeId, hostname, remoteAddress, connectedAt}` |
| `NodeDisconnected` | `{nodeId}`                                   |

## REST API Reference

### MetricsCollector (per-machine)

| Method | Path                    | Description            |
|--------|-------------------------|------------------------|
| GET    | `/api/metrics/status`   | Get collector status   |
| POST   | `/api/metrics/start`    | Start metric collection |
| POST   | `/api/metrics/stop`     | Stop metric collection |
| GET    | `/health`               | Health check           |

**POST /api/metrics/start body:**
```json
{ "intervalMs": 2000 }
```

### MetricsHub

| Method | Path         | Description              |
|--------|--------------|--------------------------|
| GET    | `/api/nodes` | Get connected collectors |
| GET    | `/health`    | Health check             |
| WS     | `/hubs/metrics` | SignalR WebSocket hub |

## Configuration Reference

### MetricsHub `appsettings.json`
```json
{
  "Akka": {
    "Port": "9080",
    "Hostname": "0.0.0.0",
    "PublicHostname": "localhost"
  },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://0.0.0.0:5000" }
    }
  }
}
```

### MetricsCollector `appsettings.json`
```json
{
  "Akka": {
    "NodeId": "node-1",
    "Port": "8081",
    "PublicHostname": "localhost",
    "HubAddress": "akka.tcp://MetricsHub@localhost:9080/user/metrics-hub"
  },
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://0.0.0.0:5001" }
    }
  }
}
```

## Firewall Requirements

| Port | Service         | Protocol |
|------|-----------------|----------|
| 5000 | Hub HTTP/SignalR | TCP     |
| 9080 | Hub Akka Remote | TCP     |
| 5001 | Collector REST  | TCP     |
| 8081 | Collector Akka  | TCP     |
| 80   | Angular UI      | TCP     |

Collectors need **outbound** access to hub port 9080.
Hub needs **inbound** access on port 9080.
