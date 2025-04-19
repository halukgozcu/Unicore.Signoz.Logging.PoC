using Unicore.Claim.Service.Services;
using Microsoft.OpenApi.Models;
using Serilog;
using Unicore.Common.OpenTelemetry.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Get service info from configuration
var serviceName = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceName") ?? "Unicore.Claim.Service";
var serviceDisplayName = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceDisplayName") ?? "Unicore Claim Service";

// Configure Serilog
builder.Host.AddUnicoreSerilog(serviceName, "1.0.0");

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Configure Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Unicore Claim Service",
        Version = "v1",
        Description = "API for processing insurance claims"
    });
    c.EnableAnnotations();
});

// Add HTTP client to call other services
builder.Services.AddHttpClient("finance", client =>
{
    client.BaseAddress = new Uri("http://localhost:1301");
    client.DefaultRequestHeaders.Add("X-Calling-Service", "Unicore.Claim.Service");
});

// Register services for dependency injection
builder.Services.AddScoped<IClaimProcessorService, ClaimProcessorService>();

// Add OpenTelemetry
builder.Services.AddUnicoreOpenTelemetry(
    builder.Configuration,
    serviceName,
    serviceDisplayName);

try
{
    var app = builder.Build();

    // Configure the HTTP request pipeline
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Unicore Claim Service v1");
            c.RoutePrefix = "swagger";
        });
    }

    // Add OpenTelemetry middleware
    app.UseUnicoreTelemetry();

    app.MapControllers();

    Log.Information("Starting Unicore Claim Service");

    // Run application
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application Unicore Claim Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
