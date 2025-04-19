using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Unicore.Common.OpenTelemetry.Enrichments;
using Unicore.Common.OpenTelemetry.Middlewares;

namespace Unicore.Common.OpenTelemetry.Configurations;

/// <summary>
/// Fluent builder for configuring telemetry
/// </summary>
public class TelemetryBuilder
{
    private readonly IServiceCollection _services;
    private readonly IConfiguration _configuration;
    private readonly string _serviceName;
    private readonly string _serviceVersion;
    private readonly string _environment;
    private bool _includeHttp = true;
    private bool _includeDatabase = true;
    private bool _includeBackgroundJobs = true;
    private bool _includeMessageBrokers = true;
    private bool _includeConsoleExport = false; // Default to not exporting to console
    private string _endpoint = "http://localhost:5317";

    internal TelemetryBuilder(
        IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion = null)
    {
        _services = services;
        _configuration = configuration;
        _serviceName = serviceName;
        _serviceVersion = serviceVersion ?? "1.0.0";
        _environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

        // Override endpoint if in configuration
        var configEndpoint = configuration.GetValue<string>("OpenTelemetry:Endpoint");
        if (!string.IsNullOrEmpty(configEndpoint))
        {
            _endpoint = configEndpoint;
        }

        // Only enable console export automatically in Development
        if (_environment.Equals("Development", StringComparison.OrdinalIgnoreCase))
        {
            _includeConsoleExport = true;
        }
    }

    /// <summary>
    /// Exclude HTTP instrumentation
    /// </summary>
    public TelemetryBuilder ExcludeHttp()
    {
        _includeHttp = false;
        return this;
    }

    /// <summary>
    /// Exclude database instrumentation
    /// </summary>
    public TelemetryBuilder ExcludeDatabase()
    {
        _includeDatabase = false;
        return this;
    }

    /// <summary>
    /// Exclude background job instrumentation
    /// </summary>
    public TelemetryBuilder ExcludeBackgroundJobs()
    {
        _includeBackgroundJobs = false;
        return this;
    }

    /// <summary>
    /// Exclude message broker instrumentation
    /// </summary> 
    public TelemetryBuilder ExcludeMessageBrokers()
    {
        _includeMessageBrokers = false;
        return this;
    }

    /// <summary>
    /// Enable console exporting for telemetry data (useful for development)
    /// </summary>
    public TelemetryBuilder WithConsoleExport(bool enable = true)
    {
        _includeConsoleExport = enable;
        return this;
    }

    /// <summary>
    /// Set custom OpenTelemetry endpoint
    /// </summary>
    public TelemetryBuilder WithEndpoint(string endpoint)
    {
        _endpoint = endpoint;
        return this;
    }

    /// <summary>
    /// Build and register all telemetry services
    /// </summary>
    public IServiceCollection Build()
    {
        // First configure dependencies
        ConfigureDependencies();

        // Then configure OpenTelemetry
        ConfigureOpenTelemetry();

        return _services;
    }

    private void ConfigureDependencies()
    {
        // Create and register the telemetry config
        var telemetryConfig = new TelemetryConfig(_serviceName, _serviceVersion, _environment);
        _services.AddSingleton(telemetryConfig);

        // Register HTTP context accessor if needed
        if (_includeHttp)
        {
            _services.AddHttpContextAccessor();
        }

        // Always register the trace enricher
        _services.AddSingleton<TraceEnricher>();

        // Register context-specific enrichers based on configuration
        if (_includeHttp)
        {
            _services.AddSingleton<HttpContextEnricher>();
        }

        if (_includeBackgroundJobs)
        {
            _services.AddSingleton<BackgroundJobEnricher>();
        }

        if (_includeMessageBrokers)
        {
            _services.AddSingleton<MessageBrokerEnricher>();
        }

        // Add the enricher registry which will be populated at runtime
        _services.AddSingleton<EnricherRegistry>();
    }

    private void ConfigureOpenTelemetry()
    {
        _services.AddOpenTelemetry()
            .WithTracing(builder =>
            {
                // Set up resource details
                builder.SetResourceBuilder(
                    ResourceBuilder.CreateDefault()
                        .AddService(
                            serviceName: _serviceName,
                            serviceVersion: _serviceVersion)
                        .AddAttributes(new Dictionary<string, object>
                        {
                            ["deployment.environment"] = _environment,
                            ["host.name"] = Environment.MachineName
                        })
                );

                // Always trace all requests
                builder.SetSampler(new AlwaysOnSampler());

                // Add sources
                builder.AddSource(_serviceName);
                builder.AddSource("BackgroundJobs");
                builder.AddSource("MessageBroker.*");

                // Add HTTP client if configured
                if (_includeHttp)
                {
                    builder.AddHttpClientInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                    });

                    builder.AddAspNetCoreInstrumentation(opts =>
                    {
                        opts.RecordException = true;
                        opts.Filter = ctx => !ctx.Request.Path.Value.Contains("/health", StringComparison.OrdinalIgnoreCase) &&
                                            !ctx.Request.Path.Value.EndsWith(".js", StringComparison.OrdinalIgnoreCase) &&
                                            !ctx.Request.Path.Value.EndsWith(".css", StringComparison.OrdinalIgnoreCase) &&
                                            !ctx.Request.Path.Value.EndsWith(".ico", StringComparison.OrdinalIgnoreCase);
                    });
                }

                // Add database instrumentation if needed
                if (_includeDatabase)
                {
                    builder.AddEntityFrameworkCoreInstrumentation(opts =>
                    {
                        opts.SetDbStatementForText = true;
                    });
                }

                // Add exporters
                if (_includeConsoleExport)
                {
                    builder.AddConsoleExporter();
                }

                builder.AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri(_endpoint);
                });
            })
            .WithMetrics(builder =>
            {
                // Set up resource details
                builder.SetResourceBuilder(
                    ResourceBuilder.CreateDefault()
                        .AddService(
                            serviceName: _serviceName,
                            serviceVersion: _serviceVersion)
                        .AddAttributes(new Dictionary<string, object>
                        {
                            ["deployment.environment"] = _environment,
                            ["host.name"] = Environment.MachineName
                        })
                );

                // Add app metrics
                builder.AddMeter(_serviceName);

                // Add system metrics
                builder.AddRuntimeInstrumentation();
                builder.AddProcessInstrumentation();

                // Add HTTP metrics if configured
                if (_includeHttp)
                {
                    builder.AddAspNetCoreInstrumentation();
                    builder.AddHttpClientInstrumentation();
                }

                // Add exporters
                if (_includeConsoleExport)
                {
                    builder.AddConsoleExporter();
                }

                builder.AddOtlpExporter(opts =>
                {
                    opts.Endpoint = new Uri(_endpoint);
                });
            });
    }
}
