using Akka.Actor;
using Akka.Configuration;
using AkkaMetrics.Hub.Actors;
using AkkaMetrics.Hub.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AkkaMetrics.Hub.Services
{
    public class AkkaHubService : IHostedService
    {
        private ActorSystem? actorSystem;
        private IActorRef? hubActor;
        private readonly IConfiguration config;
        private readonly ILogger<AkkaHubService> logger;
        private readonly IServiceProvider serviceProvider;

        public IActorRef HubActor => hubActor
            ?? throw new InvalidOperationException("Akka hub not started");

        public AkkaHubService(
            IConfiguration config,
            ILogger<AkkaHubService> logger,
            IServiceProvider serviceProvider)
        {
            this.config = config;
            this.logger = logger;
            this.serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            var hubPort = int.Parse(config["Akka:Port"] ?? "9080");
            var hostname = config["Akka:Hostname"] ?? "0.0.0.0";
            var publicHostname = config["Akka:PublicHostname"] ?? "localhost";

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
                            port = {hubPort}
                            hostname = ""{hostname}""
                            public-hostname = ""{publicHostname}""
                        }}
                    }}
                    loglevel = INFO
                    log-config-on-start = off
                }}
            ");

            actorSystem = ActorSystem.Create("MetricsHub", akkaConfig);

            // Resolve SignalR hub context from DI
            var signalRContext = serviceProvider
                .GetRequiredService<IHubContext<MetricsSignalRHub>>();

            hubActor = actorSystem.ActorOf(
                Props.Create(() => new MetricsHubActor(signalRContext)),
                "metrics-hub"
            );

            logger.LogInformation(
                "MetricsHub actor system started. Listening on akka.tcp://MetricsHub@{Host}:{Port}",
                publicHostname, hubPort);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (actorSystem != null)
            {
                await actorSystem.Terminate();
                logger.LogInformation("MetricsHub ActorSystem terminated");
            }
        }
    }
}
