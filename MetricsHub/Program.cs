using AkkaMetrics.Collector.Services;
using AkkaMetrics.Hub.Hubs;
using AkkaMetrics.Hub.Services;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Events;

namespace AkkaMetrics.Hub;

internal class Program {

  public static int Main(string[] args) {

    Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .MinimumLevel.Override("Akka", LogEventLevel.Information)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProperty("Application", "MetricsHub")
                .WriteTo.Console(
                                 outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .WriteTo.Seq(
                             serverUrl: Environment.GetEnvironmentVariable("Seq__Endpoint") ?? "http://localhost:8080",
                             apiKey: Environment.GetEnvironmentVariable("Seq__ApiKey"))
                .CreateLogger();

    try {
      Log.Information("Starting MetricsHub");
      var builder = WebApplication.CreateBuilder(args);

      // Use Serilog for all logging
      builder.Host.UseSerilog();

      builder.Configuration
             .AddJsonFile("appsettings.json", optional: true)
             .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
             .AddEnvironmentVariables()
             .AddCommandLine(args);

      builder.Services.AddControllers();
      builder.Services.AddSignalR();
      builder.Services.AddEndpointsApiExplorer();

      // Akka service (Hub actor + SignalR bridge)
      builder.Services.AddSingleton<AkkaService>();
      builder.Services.AddHostedService(sp => sp.GetRequiredService<AkkaService>());

      var otlpEndpoint = // Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ??
                         "http://localhost:5341/ingest/otlp/v1/traces";

      Log.Information("MetricsHub otlpEndpoint {OtlpEndpoint}", otlpEndpoint);

      builder.Services.AddOpenTelemetry()
             .ConfigureResource(res => res
                                      .AddService("MetricsHub")
                                      .AddAttributes(new Dictionary<string, object> {
                                                                                        ["deployment.environment"] = builder.Environment.EnvironmentName,
                                                                                        ["service.version"] = "1.0.0"
                                                                                    }))
             .WithTracing(tracing => tracing
                                    .AddAspNetCoreInstrumentation(opts => {
                                                                    opts.RecordException = true;
                                                                    opts.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                                                                  })
                                    .AddHttpClientInstrumentation()
                                    .AddConsoleExporter()
                                    .AddSource("AkkaMetrics.Hub")
                                    .AddOtlpExporter(opts => {
                                                       opts.Endpoint = new Uri(otlpEndpoint);
                                                       opts.Protocol = OtlpExportProtocol.HttpProtobuf;
                                                     }))
             .WithMetrics(metrics => metrics
                                    .AddAspNetCoreInstrumentation()
                                    .AddHttpClientInstrumentation()
                                    .AddMeter("AkkaMetrics.Hub")
                                    .AddOtlpExporter(opts => {
                                                       opts.Endpoint = new Uri(otlpEndpoint);
                                                       opts.Protocol = OtlpExportProtocol.HttpProtobuf;
                                                     }));

      builder.Services.AddSwaggerGen(c => {
                                       c.SwaggerDoc("v1", new() {
                                                                    Title = "Metrics Hub API",
                                                                    Version = "v1",
                                                                    Description = "Central hub receiving metrics from collectors"
                                                                });
                                     });

      // SignalR for pushing data to Angular UI
      builder.Services.AddSignalR(options => {
                                    options.EnableDetailedErrors = true;
                                    options.MaximumReceiveMessageSize = 102400; // 100KB
                                  });

      // Akka service
      builder.Services.AddSingleton<AkkaHubService>();
      builder.Services.AddHostedService(sp => sp.GetRequiredService<AkkaHubService>());

      builder.Services.AddCors(options => {
                                 options.AddPolicy("AllowAngular", policy =>
                                                                       policy
                                                                          .WithOrigins(
                                                                                       "http://localhost:4200",
                                                                                       "http://localhost:3000",
                                                                                       "http://localhost:80"
                                                                                      )
                                                                          .AllowAnyMethod()
                                                                          .AllowAnyHeader()
                                                                          .AllowCredentials()); // Required for SignalR
                               });

      var app = builder.Build();

      app.UseSwagger();
      app.UseSwaggerUI(c => {
                         c.SwaggerEndpoint("/swagger/v1/swagger.json", "Metrics Hub API v1");
                         c.RoutePrefix = "swagger";
                       });

      app.UseSerilogRequestLogging(opts => {
                                     opts.GetLevel = (ctx,
                                                      _,
                                                      ex) =>
                                                         ex != null                    ? LogEventLevel.Error :
                                                         ctx.Response.StatusCode > 499 ? LogEventLevel.Error :
                                                         ctx.Response.StatusCode > 399 ? LogEventLevel.Warning :
                                                                                         LogEventLevel.Information;

                                     opts.EnrichDiagnosticContext = (diagCtx,
                                                                     httpCtx) => {
                                                                      diagCtx.Set("RequestHost", httpCtx.Request.Host.Value);
                                                                      diagCtx.Set("UserAgent", httpCtx.Request.Headers["User-Agent"].ToString());
                                                                    };
                                   });

      app.UseCors("AllowAngular");
      app.MapControllers();

      // SignalR endpoint
      app.MapHub<MetricsSignalRHub>("/hubs/metrics");

      app.MapGet("/health", () => new { status = "healthy", service = "MetricsHub" });

      Log.Information("MetricsHub ready. Listening on {Urls}", string.Join(", ", app.Urls));

      app.Run();
    } catch (Exception ex) {
      Log.Fatal(ex, "MetricsHub terminated unexpectedly");

      return 1;
    }
    finally {
      Log.CloseAndFlush();
    }

    return 0;
  }
}