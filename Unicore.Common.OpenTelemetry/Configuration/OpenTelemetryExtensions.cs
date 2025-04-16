using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Serilog;
using Serilog.Exceptions;
using Serilog.Sinks.PeriodicBatching;
using Unicore.Common.OpenTelemetry.Middleware;
using Unicore.Common.OpenTelemetry.Services;

namespace Unicore.Common.OpenTelemetry.Configuration;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddUnicoreOpenTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceDisplayName)
    {
        // Add HTTP Context accessor for enriching logs with request details
        services.AddHttpContextAccessor();

        // Configure OpenTelemetry
        var telemetryConfig = new TelemetryConfig(serviceName, serviceDisplayName);
        services.AddSingleton(telemetryConfig);

        // Register Serilog LogEnricher to get trace context in logs
        services.AddSingleton<LogEnricher>();
        services.AddHostedService<LogEnricher>(provider => provider.GetRequiredService<LogEnricher>());

        // Configure OpenTelemetry integration
        services.AddOpenTelemetry()
            .WithTracing(tracerProviderBuilder => ConfigureTracing(tracerProviderBuilder, telemetryConfig))
            .WithMetrics(meterProviderBuilder => ConfigureMetrics(meterProviderBuilder, telemetryConfig));

        return services;
    }

    public static IHostBuilder AddUnicoreSerilog(
        this IHostBuilder hostBuilder,
        string serviceName,
        string? applicationVersion = null)
    {
        return hostBuilder.UseSerilog((context, services, loggerConfiguration) =>
        {
            var version = applicationVersion ?? typeof(OpenTelemetryExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0";
            var env = context.HostingEnvironment.EnvironmentName;
            var otlpEndpoint = context.Configuration.GetValue<string>("OpenTelemetry:Endpoint") ?? "http://localhost:5417";

            loggerConfiguration
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithThreadId()
                .Enrich.WithProcessId()
                .Enrich.WithEnvironmentName()
                .Enrich.WithExceptionDetails()
                .Enrich.WithProperty("ServiceName", serviceName)
                .Enrich.WithProperty("ServiceVersion", version)
                .Enrich.WithProperty("Environment", env)
                // Add timestamp in multiple formats
                .Enrich.WithProperty("TimestampUtc", DateTimeOffset.UtcNow)
                .Enrich.WithProperty("UnixTimestamp", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())
                // Output template with extended information
                .WriteTo.Console(outputTemplate:
                    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] " +
                    "[{Level:u3}] " +
                    "[{ServiceName}] " +
                    "[{Environment}] " +
                    "[TraceId: {TraceId}] " +
                    "[SpanId: {SpanId}] " +
                    "[{ThreadId}] " +
                    "[{SourceContext}] " +
                    "{Message:lj}" +
                    "{NewLine}{Exception}" +
                    "{NewLine}Properties: " +
                    "ThreadId={ThreadId}, " +
                    "ProcessId={ProcessId}, " +
                    "MachineName={MachineName}" +
                    "{NewLine}")
                .WriteTo.OpenTelemetry(options =>
                {
                    options.ResourceAttributes = new Dictionary<string, object>
                    {
                        ["service.name"] = serviceName,
                        ["service.version"] = version,
                        ["service.instance.id"] = Environment.MachineName,
                        ["deployment.environment"] = env,
                        ["host.name"] = Environment.MachineName,
                        ["os.type"] = Environment.OSVersion.Platform.ToString(),
                        ["process.runtime.name"] = ".NET",
                        ["process.runtime.version"] = Environment.Version.ToString()
                    };
                    options.Endpoint = otlpEndpoint;
                    // Remove BatchSizeLimitBytes and ExportIntervalMilliseconds
                    // Use correct batching options
                    options.Protocol = Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc;
                });

            // Add debug output in development
            if (context.HostingEnvironment.IsDevelopment())
            {
                loggerConfiguration.WriteTo.Debug();
            }

            // Add file logging with rolling interval
            loggerConfiguration.WriteTo.File(
                path: $"logs/{serviceName}-{DateTime.UtcNow:yyyy-MM-dd}.log",
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                fileSizeLimitBytes: 5 * 1024 * 1024, // 5MB
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} " +
                    "[{Level:u3}] " +
                    "{ServiceName} " +
                    "{Environment} " +
                    "TraceId:{TraceId} " +
                    "SpanId:{SpanId} " +
                    "RequestId:{RequestId} " +
                    "RequestPath:{RequestPath} " +
                    "{Message:lj} " +
                    "{NewLine}{Exception}");
        });
    }

    public static IApplicationBuilder UseUnicoreTelemetry(this IApplicationBuilder app)
    {
        // Add request ID middleware
        app.UseMiddleware<RequestIdMiddleware>();

        // Add request logging enrichment middleware
        app.Use(async (context, next) =>
        {
            // Push properties to the LogContext so they're available for all log calls in this request
            using (Serilog.Context.LogContext.PushProperty("ClientIp", context.Connection.RemoteIpAddress?.ToString() ?? "unknown"))
            using (Serilog.Context.LogContext.PushProperty("RequestPath", context.Request.Path))
            using (Serilog.Context.LogContext.PushProperty("RequestMethod", context.Request.Method))
            using (Serilog.Context.LogContext.PushProperty("UserAgent", context.Request.Headers["User-Agent"].ToString()))
            using (Serilog.Context.LogContext.PushProperty("RequestId", context.TraceIdentifier))
            {
                // Set activity tags for better trace context
                if (Activity.Current != null)
                {
                    Activity.Current.SetTag("http.client_ip", context.Connection.RemoteIpAddress?.ToString());
                    Activity.Current.SetTag("http.request_id", context.TraceIdentifier);
                    Activity.Current.SetTag("service.instance_id", Environment.MachineName);
                }

                await next();
            }
        });

        // Add Serilog request logging
        app.UseSerilogRequestLogging(opts =>
        {
            opts.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
            {
                diagnosticContext.Set("ClientIp", httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown");
                diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].ToString());
                diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
                diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
            };
        });

        return app;
    }

    private static TracerProviderBuilder ConfigureTracing(TracerProviderBuilder builder, TelemetryConfig config)
    {
        // Get configuration from DI
        var serviceProvider = builder.GetType().GetProperty("Services")?.GetValue(builder) as IServiceProvider;
        var configuration = serviceProvider?.GetService<IConfiguration>();

        // Get OpenTelemetry endpoint from configuration
        var otlpEndpoint = configuration?.GetValue<string>("OpenTelemetry:Endpoint") ?? "http://localhost:5417";

        return builder
            .SetResourceBuilder(config.ResourceBuilder)
            .SetSampler(new AlwaysOnSampler())
            .AddSource(config.ActivitySource.Name)
            .AddHttpClientInstrumentation(options =>
            {
                options.RecordException = true;
                options.EnrichWithHttpRequestMessage = (activity, request) =>
                {
                    activity.SetTag("http.request.header.traceparent", request.Headers.Contains("traceparent")
                        ? request.Headers.GetValues("traceparent").FirstOrDefault()
                        : "not-set");

                    if (request.Headers.Contains("X-Correlation-ID"))
                    {
                        activity.SetTag("correlation_id", request.Headers.GetValues("X-Correlation-ID").FirstOrDefault());
                    }

                    if (request.Headers.Contains("X-Request-ID"))
                    {
                        activity.SetTag("request_id", request.Headers.GetValues("X-Request-ID").FirstOrDefault());
                    }

                    if (request.Content != null)
                    {
                        activity.SetTag("http.request.content_length", request.Content.Headers.ContentLength);
                    }

                    activity.SetTag("http.request.host", request.RequestUri?.Host);
                };
                options.EnrichWithException = (activity, exception) =>
                {
                    activity.SetStatus(Status.Error.WithDescription(exception.Message));
                    activity.SetTag("error.type", exception.GetType().FullName);
                    activity.SetTag("error.stack_trace", exception.StackTrace);
                    if (exception.InnerException != null)
                    {
                        activity.SetTag("error.inner_exception", exception.InnerException.Message);
                    }
                };
            })
            .AddAspNetCoreInstrumentation(options =>
            {
                options.RecordException = true;
                options.Filter = (httpContext) =>
                {
                    // Don't record health check endpoints or static files
                    var path = httpContext.Request.Path.Value ?? string.Empty;
                    return !(path.Contains("/health", StringComparison.OrdinalIgnoreCase) ||
                             path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
                             path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
                             path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase));
                };
                options.EnrichWithHttpRequest = (activity, request) =>
                {
                    activity.SetTag("http.client_ip", request.HttpContext.Connection.RemoteIpAddress?.ToString());
                    activity.SetTag("http.request_id", request.HttpContext.TraceIdentifier);
                    activity.SetTag("http.user_agent", request.Headers.UserAgent);
                    activity.SetTag("http.request_content_length", request.ContentLength);
                    activity.SetTag("http.request_content_type", request.ContentType);
                    activity.SetTag("http.route", request.RouteValues.Count > 0
                        ? string.Join("/", request.RouteValues.Select(x => $"{x.Key}={x.Value}"))
                        : request.Path);

                    if (request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
                    {
                        activity.SetTag("http.forwarded_for", forwardedFor);
                    }

                    if (request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
                    {
                        activity.SetTag("correlation_id", correlationId);
                    }

                    activity.SetTag("service.name", config.ServiceName);
                };
                options.EnrichWithHttpResponse = (activity, response) =>
                {
                    activity.SetTag("http.response_content_length", response.ContentLength);
                    activity.SetTag("http.response_content_type", response.ContentType);

                    if (response.StatusCode >= 400)
                    {
                        activity.SetStatus(Status.Error.WithDescription($"HTTP {response.StatusCode}"));
                        config.FailedRequests.Add(1);
                    }
                    else
                    {
                        config.SuccessfulRequests.Add(1);
                    }
                };
                options.EnrichWithException = (activity, exception) =>
                {
                    activity.SetStatus(Status.Error.WithDescription(exception.Message));
                    activity.SetTag("error.type", exception.GetType().FullName);
                    activity.SetTag("error.stack_trace", exception.StackTrace);
                    if (exception.InnerException != null)
                    {
                        activity.SetTag("error.inner_exception", exception.InnerException.Message);
                    }
                };
            })
            .AddEntityFrameworkCoreInstrumentation(options =>
            {
                options.SetDbStatementForText = true;
                options.SetDbStatementForStoredProcedure = true;
            })
            .AddConsoleExporter()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
    }

    private static MeterProviderBuilder ConfigureMetrics(MeterProviderBuilder builder, TelemetryConfig config)
    {
        // Get configuration from DI
        var serviceProvider = builder.GetType().GetProperty("Services")?.GetValue(builder) as IServiceProvider;
        var configuration = serviceProvider?.GetService<IConfiguration>();

        // Get OpenTelemetry endpoint from configuration
        var otlpEndpoint = configuration?.GetValue<string>("OpenTelemetry:Endpoint") ?? "http://localhost:5417";

        return builder
            .SetResourceBuilder(config.ResourceBuilder)
            .AddMeter(config.Meter.Name)
            .AddRuntimeInstrumentation()
            .AddProcessInstrumentation()
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddConsoleExporter()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
            });
    }
}
