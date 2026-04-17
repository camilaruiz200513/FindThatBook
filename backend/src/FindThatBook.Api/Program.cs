using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FindThatBook.Api.Middleware;
using FindThatBook.Core;
using FindThatBook.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "FindThatBook API",
        Version = "v1",
        Description = "LLM-assisted search over Open Library for noisy book queries.",
    });

    var xmlFile = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xmlFile))
    {
        options.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
    }
});

const string FrontendCorsPolicy = "FrontendCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173", "http://localhost:4173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders(CorrelationIdMiddleware.HeaderName));
});

// Rate limiting: 20 req/min per client IP, with a small burst via token bucket.
// Prevents a single misbehaving client (or a runaway test) from hammering
// Gemini free-tier or Open Library. Configured once here so it stays visible.
const string PublicRateLimitPolicy = "public";
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(PublicRateLimitPolicy, httpContext =>
    {
        var key = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetTokenBucketLimiter(key, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = 20,
            TokensPerPeriod = 20,
            ReplenishmentPeriod = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
});

builder.Services.AddCore(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseCorrelationId();
app.UseExceptionHandling();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "FindThatBook API v1"));
}

app.UseHttpsRedirection();
app.UseCors(FrontendCorsPolicy);

app.UseRateLimiter();

// Liveness: used by the frontend and any orchestrator. Kept as a top-level
// minimal endpoint (no rate limit, no rewriting) so it never blocks when the
// app itself is healthy but the rate limiter is saturated.
app.MapGet("/health", () => Results.Ok(new { status = "ok", timestamp = DateTimeOffset.UtcNow }))
    .WithName("Health");

app.MapControllers().RequireRateLimiting(PublicRateLimitPolicy);

app.Run();

public partial class Program;
