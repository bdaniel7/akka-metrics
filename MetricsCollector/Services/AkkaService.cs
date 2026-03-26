using Akka.Actor;
using Akka.Configuration;
using AkkaMetrics.Collector.Actors;

namespace AkkaMetrics.Collector.Services
{
    public class AkkaService : IHostedService
    {
        ActorSystem? actorSystem;
        IActorRef? collectorActor;
        readonly IConfiguration config;
        readonly ILogger<AkkaService> logger;

        public IActorRef CollectorActor => collectorActor
            ?? throw new InvalidOperationException("Akka system not started");

        public AkkaService(IConfiguration config, ILogger<AkkaService> logger)
        {
            this.config = config;
            this.logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var nodeId = config["Akka:NodeId"] ?? $"node-{Environment.MachineName}";
            var collectorPort = int.Parse(config["Akka:Port"] ?? "8081");
            var hubAddress = config["Akka:HubAddress"]
                ?? "akka.tcp://MetricsHub@10.0.0.42:9080/user/metrics-hub";

            var akkaConfig = ConfigurationFactory.ParseString($@"
                akka {{
                    actor {{
                        provider = remote
                        serializers {{
                            json = ""Akka.Serialization.NewtonSoftJsonSerializer, Akka""
                        }}
                        serialization-bindings {{
                            ""System.Object"" = json
                        }}
                    }}
                    remote {{
                        dot-netty.tcp {{
                            port = {collectorPort}
                            hostname = ""0.0.0.0""
                            public-hostname = ""{config["Akka:PublicHostname"] ?? "localhost"}""
                        }}
                    }}
                    loglevel = INFO
                    log-config-on-start = off
                }}
            ");

            actorSystem = ActorSystem.Create("MetricsCollector", akkaConfig);

            collectorActor = actorSystem.ActorOf(
                Props.Create(() => new MetricsCollectorActor(nodeId, hubAddress)),
                "metrics-collector"
            );

            logger.LogInformation("Akka ActorSystem 'MetricsCollector' started on port {Port}", collectorPort);
            logger.LogInformation("Node ID: {NodeId}", nodeId);
            logger.LogInformation("Hub Address: {HubAddress}", hubAddress);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (actorSystem != null)
            {
                await actorSystem.Terminate();
                logger.LogInformation("Akka ActorSystem terminated");
            }
        }
    }
}
