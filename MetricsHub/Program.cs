using AkkaMetrics.Hub.Hubs;
using AkkaMetrics.Hub.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Metrics Hub API",
        Version = "v1",
        Description = "Central hub receiving metrics from collectors"
    });
});

// SignalR for pushing data to Angular UI
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;
    options.MaximumReceiveMessageSize = 102400; // 100KB
});

// Akka service
builder.Services.AddSingleton<AkkaHubService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AkkaHubService>());

builder.Services.AddCors(options =>
{
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

// ── Middleware ─────────────────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Metrics Hub API v1");
    c.RoutePrefix = "swagger";
});

app.UseCors("AllowAngular");
app.MapControllers();

// SignalR endpoint
app.MapHub<MetricsSignalRHub>("/hubs/metrics");

app.MapGet("/health", () => new { status = "healthy", service = "MetricsHub" });

app.Run();
