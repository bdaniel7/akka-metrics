using AkkaMetrics.Collector.Services;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;


namespace AkkaMetrics.Collector;

internal class Program {
  public static int Main(string[] args) {

    Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Akka", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "MetricsCollector")
                .WriteTo.Console(
                                 outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .WriteTo.Seq(
                             serverUrl: Environment.GetEnvironmentVariable("Seq__Endpoint") ?? "http://localhost:8080",
                             apiKey: Environment.GetEnvironmentVariable("Seq__ApiKey"))
                .CreateLogger();

    try {

      var builder = WebApplication.CreateBuilder(args);

      // Use Serilog
      builder.Host.UseSerilog();

      builder.Configuration
             .AddJsonFile("appsettings.json", optional: true)
             .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
             .AddEnvironmentVariables()
             .AddCommandLine(args);


      var config = builder.Configuration;
      //var nodeId = Environment.GetEnvironmentVariable("Akka__NodeId") ?? "unknown";
      var nodeId = config["Akka:NodeId"] ?? $"node-{Environment.MachineName}";
      Log.Information("Starting MetricsCollector {NodeId}", nodeId);


      builder.Services.AddControllers();
      builder.Services.AddEndpointsApiExplorer();

      builder.Services.AddSwaggerGen(c => {
                                       c.SwaggerDoc("v1", new() {
                                                                    Title = "Metrics Collector API",
                                                                    Version = "v1",
                                                                    Description = "Start/stop metric collection on this node"
                                                                });
                                     });

      // Register Akka service as singleton so controllers can access the actor ref
      builder.Services.AddSingleton<AkkaService>();
      builder.Services.AddHostedService(sp => sp.GetRequiredService<AkkaService>());

      builder.Services.AddCors(options => {
                                 options.AddDefaultPolicy(policy =>
                                                              policy.AllowAnyOrigin()
                                                                    .AllowAnyMethod()
                                                                    .AllowAnyHeader());
                               });


      var otlpEndpoint = // Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ??
                         "http://localhost:5341/ingest/otlp/v1/traces";

      Log.Information("MetricsHub otlpEndpoint {OtlpEndpoint}", otlpEndpoint);

      builder.Services.AddOpenTelemetry()
             .ConfigureResource(res => res
                                      .AddService("MetricsCollector")
                                      .AddAttributes(new Dictionary<string, object>
                                                     {
                                                         ["deployment.environment"] = builder.Environment.EnvironmentName,
                                                         ["service.version"] = "1.0.0",
                                                         ["node.id"] = nodeId
                                                     }))
             .WithTracing(tracing => tracing
                                    .AddAspNetCoreInstrumentation(opts =>
                                                                  {
                                                                    opts.RecordException = true;
                                                                    opts.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                                                                  })
                                    .AddHttpClientInstrumentation()
                                    .AddConsoleExporter()
                                    .AddSource("AkkaMetrics.Collector")
                                    .AddOtlpExporter(opts => {
                                                       opts.Endpoint = new Uri(otlpEndpoint);
                                                       opts.Protocol = OtlpExportProtocol.HttpProtobuf;
                                                     }))
             .WithMetrics(metrics => metrics
                                    .AddAspNetCoreInstrumentation()
                                    .AddHttpClientInstrumentation()
                                    .AddMeter("AkkaMetrics.Collector")
                                    .AddOtlpExporter(opts => {
                                                       opts.Endpoint = new Uri(otlpEndpoint);
                                                       opts.Protocol = OtlpExportProtocol.HttpProtobuf;
                                                     }));

      var app = builder.Build();

      app.UseSwagger();
      app.UseSwaggerUI(c => {
                         c.SwaggerEndpoint("/swagger/v1/swagger.json", "Metrics Collector API v1");
                         c.RoutePrefix = string.Empty;
                       });

      app.UseSerilogRequestLogging(opts =>
                                   {
                                     opts.GetLevel = (ctx, _, ex) =>
                                                         ex != null                    ? LogEventLevel.Error :
                                                         ctx.Response.StatusCode > 499 ? LogEventLevel.Error :
                                                         ctx.Response.StatusCode > 399 ? LogEventLevel.Warning :
                                                                                         LogEventLevel.Debug;  // Debug level for normal requests
                                     opts.EnrichDiagnosticContext = (diagCtx, httpCtx) =>
                                                                    {
                                                                      diagCtx.Set("RequestHost", httpCtx.Request.Host.Value);
                                                                      diagCtx.Set("NodeId", nodeId);
                                                                    };
                                   });

      app.UseCors();
      app.MapControllers();

      app.MapGet("/health", () => new { status = "healthy", service = "MetricsCollector", nodeId });

      Log.Information("MetricsCollector {NodeId} ready. Listening on {Urls}", nodeId, string.Join(", ", app.Urls));

      app.Run();
    } catch (Exception ex) {
      Log.Fatal(ex, "MetricsCollector terminated unexpectedly");

      return 1;
    }
    finally {
      Log.CloseAndFlush();
    }

    return 0;
  }
}