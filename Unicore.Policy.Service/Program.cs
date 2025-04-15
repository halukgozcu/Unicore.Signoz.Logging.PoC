using Unicore.Policy.Service.Services;
using Unicore.Common.OpenTelemetry.Configuration;
using Microsoft.OpenApi.Models;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Get service info from configuration
var serviceName = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceName") ?? "Unicore.Policy.Service";
var serviceDisplayName = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceDisplayName") ?? "Unicore Policy Service";

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
        Title = "Unicore Policy Service",
        Version = "v1",
        Description = "API for managing insurance policies"
    });
    c.EnableAnnotations();
});

// Register services for dependency injection
builder.Services.AddScoped<IPolicyService, PolicyManagementService>();

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
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Unicore Policy Service v1");
            c.RoutePrefix = "swagger";
        });
    }

    // Add OpenTelemetry middleware
    app.UseUnicoreTelemetry();

    app.MapControllers();

    Log.Information("Starting Unicore Policy Service on port 1202");

    // Run application on specific port
    await app.RunAsync("http://localhost:1202");
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application Unicore Policy Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
