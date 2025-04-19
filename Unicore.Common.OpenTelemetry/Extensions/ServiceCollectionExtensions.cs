using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Unicore.Common.OpenTelemetry.Configurations;
using Unicore.Common.OpenTelemetry.Enrichments;
using Unicore.Common.OpenTelemetry.Middlewares;

namespace Unicore.Common.OpenTelemetry.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add telemetry with a fluent builder
    /// </summary>
    public static TelemetryBuilder AddTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion = null)
    {
        return new TelemetryBuilder(services, configuration, serviceName, serviceVersion);
    }

    /// <summary>
    /// Configure logging with a fluent builder
    /// </summary>
    public static LoggingBuilder ConfigureLogging(
        this IHostBuilder hostBuilder,
        string serviceName,
        string serviceVersion = null)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var version = serviceVersion ?? "1.0.0";

        // Check if Hangfire is present to automatically configure its logging
        bool hangfirePresent = AppDomain.CurrentDomain.GetAssemblies()
            .Any(a => a.GetName().Name?.StartsWith("Hangfire", StringComparison.OrdinalIgnoreCase) == true);

        var builder = new LoggingBuilder(hostBuilder, serviceName, version, environment);

        // Auto-configure Hangfire if present
        if (hangfirePresent)
        {
            builder.WithHangfireLogging(true,
                // Set a more verbose level in Development
                environment.Equals("Development", StringComparison.OrdinalIgnoreCase)
                    ? LogEventLevel.Debug
                    : LogEventLevel.Information);
        }

        return builder;
    }

    /// <summary>
    /// Use telemetry middleware in the application
    /// </summary>
    public static IApplicationBuilder UseCorrelatedTelemetry(this IApplicationBuilder app)
    {
        // Add request ID middleware
        app.UseMiddleware<RequestIdMiddleware>();

        // Configure the enricher registry
        var registry = app.ApplicationServices.GetService<EnricherRegistry>();
        if (registry == null) return app;

        // Add all available enrichers to the registry
        var traceEnricher = app.ApplicationServices.GetService<TraceEnricher>();
        if (traceEnricher != null)
        {
            registry.Add(traceEnricher);
        }

        var httpContextEnricher = app.ApplicationServices.GetService<HttpContextEnricher>();
        if (httpContextEnricher != null)
        {
            registry.Add(httpContextEnricher);
        }

        var backgroundJobEnricher = app.ApplicationServices.GetService<BackgroundJobEnricher>();
        if (backgroundJobEnricher != null)
        {
            registry.Add(backgroundJobEnricher);
        }

        var messageBrokerEnricher = app.ApplicationServices.GetService<MessageBrokerEnricher>();
        if (messageBrokerEnricher != null)
        {
            registry.Add(messageBrokerEnricher);
        }

        // Add Serilog request logging
        app.UseSerilogRequestLogging(opts =>
        {
            // Configure request logging options
            opts.GetLevel = (httpContext, elapsed, ex) =>
            {
                if (ex != null)
                {
                    return LogEventLevel.Error;
                }

                if (httpContext.Response.StatusCode >= 500)
                {
                    return LogEventLevel.Error;
                }

                if (httpContext.Response.StatusCode >= 400)
                {
                    return LogEventLevel.Warning;
                }

                if (elapsed > 5000) // 5 seconds
                {
                    return LogEventLevel.Warning;
                }

                return LogEventLevel.Information;
            };

            opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].ToString());
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
                diagnosticContext.Set("RouteValues", httpContext.Request.RouteValues);
            };
        });

        return app;
    }
}
