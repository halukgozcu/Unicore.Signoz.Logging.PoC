using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Resources;

namespace Unicore.Common.OpenTelemetry.Configuration;

public class TelemetryConfig
{
    public string ServiceName { get; }
    public string DisplayName { get; }

    // Metrics
    public readonly Meter Meter;
    public readonly Counter<long> RequestCounter;
    public readonly Counter<long> SuccessfulRequests;
    public readonly Counter<long> FailedRequests;
    public readonly Histogram<double> RequestDuration;

    // Tracing
    public readonly ActivitySource ActivitySource;

    // Resource builder for common attributes
    public readonly ResourceBuilder ResourceBuilder;

    public TelemetryConfig(string serviceName, string displayName)
    {
        ServiceName = serviceName;
        DisplayName = displayName;

        // Initialize metrics
        Meter = new Meter(serviceName);
        RequestCounter = Meter.CreateCounter<long>("app.request.counter", description: "Counts the number of requests");
        SuccessfulRequests = Meter.CreateCounter<long>("app.successful_requests", description: "Counts the number of successful requests");
        FailedRequests = Meter.CreateCounter<long>("app.failed_requests", description: "Counts the number of failed requests");
        RequestDuration = Meter.CreateHistogram<double>("app.request_duration", unit: "ms", description: "Duration of requests");

        // Initialize activity source for manual tracing
        ActivitySource = new ActivitySource(serviceName);

        // Initialize resource builder
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var instanceId = Environment.MachineName;
        var version = GetType().Assembly.GetName().Version?.ToString() ?? "1.0.0";

        ResourceBuilder = ResourceBuilder
            .CreateDefault()
            .AddService(serviceName: serviceName, serviceVersion: version)
            .AddAttributes(new Dictionary<string, object>
            {
                ["service.display_name"] = displayName,
                ["service.instance.id"] = instanceId,
                ["deployment.environment"] = environment,
                ["host.name"] = Environment.MachineName,
                ["os.type"] = Environment.OSVersion.Platform.ToString(),
                ["os.version"] = Environment.OSVersion.VersionString
            })
            .AddTelemetrySdk()
            .AddEnvironmentVariableDetector();
    }
}
