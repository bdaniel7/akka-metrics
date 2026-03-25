using AkkaMetrics.Collector.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ──────────────────────────────────────────────────────────────
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
        Title = "Metrics Collector API",
        Version = "v1",
        Description = "Start/stop metric collection on this node"
    });
});

// Register Akka service as singleton so controllers can access the actor ref
builder.Services.AddSingleton<AkkaService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<AkkaService>());

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

// ── Middleware ─────────────────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Metrics Collector API v1");
    c.RoutePrefix = string.Empty;
});

app.UseCors();
app.MapControllers();

app.MapGet("/health", () => new { status = "healthy", service = "MetricsCollector" });

app.Run();
