using Unicore.Finance.Service.Services;
using Unicore.Common.OpenTelemetry.Configurations;
using Microsoft.OpenApi.Models;
using Serilog;
using Unicore.Common.OpenTelemetry.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Get service info from configuration
var serviceName = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceName") ?? "Unicore.Finance.Service";
var serviceDisplayName = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceDisplayName") ?? "Unicore Finance Service";

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
        Title = "Unicore Finance Service",
        Version = "v1",
        Description = "API for processing insurance payments"
    });
    c.EnableAnnotations();
});

// Add HTTP client to call other services
builder.Services.AddHttpClient("policy", client =>
{
    client.BaseAddress = new Uri("http://localhost:1302"); // Updated to match Policy service port in launchSettings.json
    client.DefaultRequestHeaders.Add("X-Calling-Service", "Unicore.Finance.Service");
});

// Register services for dependency injection
builder.Services.AddScoped<IPaymentService, PaymentService>();

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
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "Unicore Finance Service v1");
            c.RoutePrefix = "swagger";
        });
    }

    // Add OpenTelemetry middleware
    app.UseUnicoreTelemetry();

    app.MapControllers();

    Log.Information("Starting Unicore Finance Service");

    // Run application using the port from launchSettings.json
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application Unicore Finance Service terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
