using Akka.Actor;
using Akka.Event;
using Akka.Streams;
using Akka.Streams.Dsl;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AkkaMetrics.Shared;

namespace AkkaMetrics.Collector.Actors
{
    /// <summary>
    /// Collects CPU and RAM metrics using Akka.Streams and pushes them to the remote Hub actor.
    /// </summary>
    public class MetricsCollectorActor : ReceiveActor
    {
        readonly ILoggingAdapter log = Context.GetLogger();
        readonly string nodeId;
        readonly string hostname;
        readonly string hubAddress;

        IActorRef? hubActor;
        IKillSwitch? killSwitch;
        ActorMaterializer? materializer;
        bool isCollecting;
        int intervalMs = 2000;
        DateTimeOffset? startedAt;

        // Cross-platform CPU tracking
        TimeSpan lastProcessCpuTime  = TimeSpan.Zero;
        DateTime lastProcessCpuCheck = DateTime.UtcNow;
        double   linuxPrevIdle  = 0;
        double   linuxPrevTotal = 0;

        // Windows CPU counter (created once, reused each tick)
        PerformanceCounter? winCpuCounter;

        public MetricsCollectorActor(string nodeId, string hubAddress)
        {
            this.nodeId = nodeId;
            hostname = System.Net.Dns.GetHostName();
            this.hubAddress = hubAddress;

            Receive<StartCollecting>(msg => handleStartCollecting(msg));
            Receive<StopCollecting>(_ => handleStopCollecting());
            Receive<GetCollectorStatus>(_ => handleGetStatus());
            Receive<CollectTick>(_ => handleCollectTick());
            Receive<HubConnected>(msg => handleHubConnected(msg));
            Receive<StreamFailed>(msg => handleStreamFailed(msg));
        }

        protected override void PreStart()
        {
            log.Info("[{NodeId}] MetricsCollectorActor starting. Hub: {Hub}", nodeId, hubAddress);
            materializer = Context.Materializer();
            initCpu();
            connectToHub();
        }

        protected override void PostStop()
        {
            killSwitch?.Shutdown();
            materializer?.Dispose();
            winCpuCounter?.Dispose();
            log.Info("[{NodeId}] MetricsCollectorActor stopped", nodeId);
        }

        void initCpu()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // Create the counter once; first call always returns 0, so call it now to prime it
                    winCpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
                    winCpuCounter.NextValue(); // prime — discard first sample
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    readLinuxCpuCounters(out linuxPrevIdle, out linuxPrevTotal);
                }
            }
            catch (Exception ex)
            {
                log.Warning("CPU counter init failed: {Msg}", ex.Message);
            }
        }

        void connectToHub()
        {
            log.Info("[{NodeId}] Resolving hub actor at {HubAddress}", nodeId, hubAddress);
            Context.ActorSelection(hubAddress)
                .ResolveOne(TimeSpan.FromSeconds(10))
                .ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully)
                        return new HubConnected(task.Result);
                    throw task.Exception ?? new Exception("Hub resolution failed");
                })
                .PipeTo(Self);
        }

        void handleHubConnected(HubConnected msg)
        {
            hubActor = msg.Hub;
            log.Info("[{NodeId}] Connected to hub actor", nodeId);
            hubActor.Tell(new RegisterCollector(nodeId, hostname));
        }

        void handleStartCollecting(StartCollecting msg)
        {
            if (isCollecting)
            {
                log.Warning("[{NodeId}] Already collecting metrics", nodeId);
                Sender.Tell(new CollectorStatus(nodeId, true, intervalMs, startedAt));
                return;
            }

            intervalMs = msg.IntervalMs;
            isCollecting = true;
            startedAt = DateTimeOffset.UtcNow;

            // Use Akka.Streams to create a ticker source
            startMetricsStream();

            log.Info("[{NodeId}] Started collecting metrics every {Interval}ms", nodeId, intervalMs);
            Sender.Tell(new CollectorStatus(nodeId, true, intervalMs, startedAt));
        }

        void startMetricsStream()
        {
            killSwitch?.Shutdown();

            var (ks, _) = Source.Tick(TimeSpan.FromMilliseconds(intervalMs), TimeSpan.FromMilliseconds(intervalMs), "tick")
                .ViaMaterialized(KillSwitches.Single<string>(), Keep.Right)
                .Select(_ => new CollectTick())
                .ToMaterialized(
                                Sink.ActorRef<CollectTick>(
                                                           Self,
                                                           onCompleteMessage: new StreamCompleted(),
                                                           onFailureMessage: ex => new StreamFailed(ex)),
                                Keep.Both)
                .Run(materializer!);

            killSwitch = ks;
        }

        void handleStopCollecting()
        {
            if (!isCollecting)
            {
                Sender.Tell(new CollectorStatus(nodeId, false, intervalMs, null));
                return;
            }

            killSwitch?.Shutdown();
            killSwitch = null;
            isCollecting = false;
            startedAt = null;

            log.Info("[{NodeId}] Stopped collecting metrics", nodeId);
            Sender.Tell(new CollectorStatus(nodeId, false, intervalMs, null));
        }

        void handleGetStatus() =>
            Sender.Tell(new CollectorStatus(nodeId, isCollecting, intervalMs, startedAt));

        void handleStreamFailed(StreamFailed msg)
        {
            log.Error(msg.Cause, "[{NodeId}] Stream failed", nodeId);
            killSwitch   = null;
            isCollecting = false;
            startedAt    = null;
        }

        void handleCollectTick()
        {
            if (hubActor == null)
            {
                log.Warning("[{NodeId}] Hub actor not connected yet", nodeId);
                return;
            }

            try
            {
                var cpu = getCpuPercent();
                getMemoryInfo(out double ramUsedMb, out double ramTotalMb);
                hubActor.Tell(new PushMetrics(new MetricSnapshot(
                    NodeId:     nodeId,
                    Hostname:   hostname,
                    CpuPercent: cpu,
                    RamUsedMb:  ramUsedMb,
                    RamTotalMb: ramTotalMb,
                    RamPercent: ramTotalMb > 0 ? ramUsedMb / ramTotalMb * 100.0 : 0,
                    Timestamp:  DateTimeOffset.UtcNow
                )));
            }
            catch (Exception ex)
            {
                log.Error(ex, "[{NodeId}] Error collecting metrics", nodeId);
            }
        }

        double getCpuPercent()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return getWindowsCpu();
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return getLinuxCpuDelta();
            return getMacCpu();
        }

        // Windows: PerformanceCounter gives the real system-wide "% Processor Time"
        double getWindowsCpu()
        {
            try
            {
                if (winCpuCounter == null) initCpu();
                float value = winCpuCounter!.NextValue();
                return Math.Clamp(value, 0, 100);
            }
            catch { return 0; }
        }

        // Linux: diff /proc/stat counters between calls
        double getLinuxCpuDelta()
        {
            try
            {
                readLinuxCpuCounters(out double idle, out double total);
                double diffIdle  = idle  - linuxPrevIdle;
                double diffTotal = total - linuxPrevTotal;
                linuxPrevIdle  = idle;
                linuxPrevTotal = total;
                if (diffTotal <= 0) return 0;
                return Math.Clamp((1.0 - diffIdle / diffTotal) * 100.0, 0, 100);
            }
            catch { return 0; }
        }

        static void readLinuxCpuCounters(out double idle, out double total)
        {
            var line  = File.ReadLines("/proc/stat").First();
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            double user    = double.Parse(parts[1]);
            double nice    = double.Parse(parts[2]);
            double system  = double.Parse(parts[3]);
            idle           = double.Parse(parts[4]);
            double iowait  = double.Parse(parts[5]);
            double irq     = double.Parse(parts[6]);
            double softirq = double.Parse(parts[7]);
            total = user + nice + system + idle + iowait + irq + softirq;
        }

        double getProcessCpuDelta()
        {
            try
            {
                var p = Process.GetCurrentProcess();
                p.Refresh();
                var now       = DateTime.UtcNow;
                var cpuNow    = p.TotalProcessorTime;
                var elapsedMs = (now - lastProcessCpuCheck).TotalMilliseconds;
                var cpuMs     = (cpuNow - lastProcessCpuTime).TotalMilliseconds;
                lastProcessCpuCheck = now;
                lastProcessCpuTime  = cpuNow;
                if (elapsedMs <= 0) return 0;
                return Math.Clamp(cpuMs / (elapsedMs * Environment.ProcessorCount) * 100.0, 0, 100);
            }
            catch { return 0; }
        }

        // macOS: parse the output of `top -l 2 -n 0` (two samples, discard first)
        static double getMacCpu()
        {
            try
            {
                using var p = new Process
                              {
                                      StartInfo = new ProcessStartInfo("top", "-l 2 -n 0")
                                                  {
                                                          RedirectStandardOutput = true,
                                                          UseShellExecute = false,
                                                          CreateNoWindow  = true
                                                  }
                              };
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                // Find last "CPU usage:" line (second sample)
                var lines = output.Split('\n');
                string? cpuLine = null;
                foreach (var l in lines)
                    if (l.TrimStart().StartsWith("CPU usage:")) cpuLine = l;
                if (cpuLine == null) return 0;
                // "CPU usage: 5.55% user, 11.11% sys, 83.33% idle"
                var idlePart = cpuLine.Split(',').FirstOrDefault(s => s.Contains("idle"));
                if (idlePart == null) return 0;
                var idleStr = idlePart.Trim().Split('%')[0].Trim();
                if (double.TryParse(idleStr, out double idle))
                    return Math.Clamp(100.0 - idle, 0, 100);
                return 0;
            }
            catch { return 0; }
        }

        // ── Memory ────────────────────────────────────────────────────────────

        static void getMemoryInfo(out double ramUsedMb, out double ramTotalMb)
        {
            ramUsedMb = 0; ramTotalMb = 0;
            try
            {
                if      (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   getLinuxMemory(out ramUsedMb, out ramTotalMb);
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) getWindowsMemory(out ramUsedMb, out ramTotalMb);
                else                                                           getMacMemory(out ramUsedMb, out ramTotalMb);
            }
            catch { }
        }

        static void getLinuxMemory(out double ramUsedMb, out double ramTotalMb)
        {
            ramUsedMb = 0; ramTotalMb = 0;
            long totalKb = 0, availKb = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if      (line.StartsWith("MemTotal:"))     totalKb = parseKbLine(line);
                else if (line.StartsWith("MemAvailable:")) availKb = parseKbLine(line);
                if (totalKb > 0 && availKb > 0) break;
            }
            ramTotalMb = totalKb / 1024.0;
            ramUsedMb  = (totalKb - availKb) / 1024.0;
        }

        static long parseKbLine(string line)
        {
            var val = line.Split(':', StringSplitOptions.TrimEntries)[1]
                          .Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
            return long.Parse(val);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct Memorystatusex
        {
            public uint  dwLength;
            public uint  dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool GlobalMemoryStatusEx(ref Memorystatusex lpBuffer);

        static void getWindowsMemory(out double ramUsedMb, out double ramTotalMb)
        {
            ramUsedMb = 0; ramTotalMb = 0;
            var ms = new Memorystatusex { dwLength = (uint)Marshal.SizeOf<Memorystatusex>() };
            if (GlobalMemoryStatusEx(ref ms))
            {
                ramTotalMb = ms.ullTotalPhys / 1024.0 / 1024.0;
                ramUsedMb  = (ms.ullTotalPhys - ms.ullAvailPhys) / 1024.0 / 1024.0;
            }
        }

        static void getMacMemory(out double ramUsedMb, out double ramTotalMb)
        {
            try
            {
                using var p = new Process
                {
                    StartInfo = new ProcessStartInfo("vm_stat")
                    {
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow  = true
                    }
                };
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();

                long pageSize = 4096;
                long free = 0, active = 0, inactive = 0, wired = 0, speculative = 0;
                foreach (var line in output.Split('\n'))
                {
                    if (line.StartsWith("Mach Virtual Memory Statistics"))
                    {
                        var m = System.Text.RegularExpressions.Regex.Match(line, @"page size of (\d+) bytes");
                        if (m.Success) pageSize = long.Parse(m.Groups[1].Value);
                    }
                    else if (line.StartsWith("Pages free:"))          free        = parseVmStatLine(line);
                    else if (line.StartsWith("Pages active:"))        active      = parseVmStatLine(line);
                    else if (line.StartsWith("Pages inactive:"))      inactive    = parseVmStatLine(line);
                    else if (line.StartsWith("Pages wired down:"))    wired       = parseVmStatLine(line);
                    else if (line.StartsWith("Pages speculative:"))   speculative = parseVmStatLine(line);
                }
                long usedPages  = active + wired;
                long totalPages = free + active + inactive + wired + speculative;
                ramTotalMb = totalPages * pageSize / 1024.0 / 1024.0;
                ramUsedMb  = usedPages  * pageSize / 1024.0 / 1024.0;
            }
            catch
            {
                var gcInfo = GC.GetGCMemoryInfo();
                ramTotalMb = gcInfo.TotalAvailableMemoryBytes / 1024.0 / 1024.0;
                ramUsedMb  = Process.GetCurrentProcess().WorkingSet64 / 1024.0 / 1024.0;
            }
        }

        static long parseVmStatLine(string line)
        {
            var s = line.Split(':')[1].Trim().TrimEnd('.');
            return long.TryParse(s, out long v) ? v : 0;
        }

        // ── Inner Messages ──────────────────────────────────────────────────────
        record CollectTick();

        record StreamCompleted();

        record StreamFailed(Exception Cause);

        record HubConnected(IActorRef Hub);

    }
}
