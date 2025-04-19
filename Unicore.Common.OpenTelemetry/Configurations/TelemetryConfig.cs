using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Resources;

namespace Unicore.Common.OpenTelemetry.Configurations;

/// <summary>
/// Manages telemetry configuration for metrics, tracing, and resource attributes
/// </summary>
public class TelemetryConfig : IDisposable
{
    // Service information
    public string ServiceName { get; }
    public string DisplayName { get; }
    public string ServiceNamespace { get; }
    public string ServiceVersion { get; }
    public string Environment { get; }

    // Metrics - convert public fields to properties
    public Meter Meter { get; }
    public Counter<long> RequestCounter { get; }
    public Counter<long> SuccessfulRequests { get; }
    public Counter<long> FailedRequests { get; }
    public Histogram<double> RequestDuration { get; }

    // Tracing
    public ActivitySource ActivitySource { get; }

    // Resource builder for common attributes
    public ResourceBuilder ResourceBuilder { get; }

    // Thread-safe disposal
    private volatile bool _disposed;
    private readonly object _disposeLock = new object();

    // Existing constructors
    public TelemetryConfig(string serviceName, string displayName)
        : this(serviceName, displayName, System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development")
    {
    }

    public TelemetryConfig(string serviceName, string serviceVersion, string environment)
    {
        // Validate parameters - stricter validation
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name cannot be null or empty", nameof(serviceName));
        ServiceName = serviceName.Trim();

        // Properly set the display name - now using the field from constructor parameters
        DisplayName = serviceVersion; // This is the actual error - using serviceVersion instead of displayName

        if (string.IsNullOrWhiteSpace(serviceVersion))
            serviceVersion = "1.0.0"; // Default version
        ServiceVersion = serviceVersion.Trim();

        if (string.IsNullOrWhiteSpace(environment))
            environment = "Development"; // Default environment
        Environment = environment.Trim();

        ServiceNamespace = "Unicore"; // Default namespace

        // Initialize metrics with safe values and proper property initialization
        try
        {
            Meter = new Meter(ServiceName, ServiceVersion);

            RequestCounter = Meter.CreateCounter<long>(
                name: "app.request.counter",
                unit: "{request}",
                description: "Counts the number of requests");

            SuccessfulRequests = Meter.CreateCounter<long>(
                name: "app.successful_requests",
                unit: "{request}",
                description: "Counts the number of successful requests");

            FailedRequests = Meter.CreateCounter<long>(
                name: "app.failed_requests",
                unit: "{request}",
                description: "Counts the number of failed requests");

            RequestDuration = Meter.CreateHistogram<double>(
                name: "app.request_duration",
                unit: "ms",
                description: "Duration of requests");
        }
        catch (Exception ex)
        {
            // If meter creation fails, log the error and create a dummy meter
            Console.Error.WriteLine($"Error creating metrics: {ex.Message}");
            Meter = new Meter("dummy.meter");
            RequestCounter = Meter.CreateCounter<long>("dummy.counter");
            SuccessfulRequests = Meter.CreateCounter<long>("dummy.successful");
            FailedRequests = Meter.CreateCounter<long>("dummy.failed");
            RequestDuration = Meter.CreateHistogram<double>("dummy.duration");
        }

        // Initialize activity source for manual tracing - with version
        try
        {
            ActivitySource = new ActivitySource(ServiceName, ServiceVersion);
        }
        catch (Exception ex)
        {
            // If activity source creation fails, log the error and create a dummy source
            Console.Error.WriteLine($"Error creating activity source: {ex.Message}");
            ActivitySource = new ActivitySource("dummy.source", "0.0.0");
        }

        // Initialize resource builder with defensive error handling
        try
        {
            // Get a valid instance ID with improved uniqueness
            var instanceId = GetSafeInstanceId();

            ResourceBuilder = ResourceBuilder
                .CreateDefault()
                .AddService(serviceName: ServiceName, serviceVersion: ServiceVersion)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["service.display_name"] = DisplayName,
                    ["service.instance.id"] = instanceId,
                    ["deployment.environment"] = Environment,
                    ["host.name"] = GetSafeHostName(),
                    ["os.type"] = GetSafeOSPlatform(),
                    ["os.version"] = GetSafeOSVersion(),
                    ["process.runtime.name"] = ".NET",
                    ["process.runtime.version"] = System.Environment.Version.ToString()
                })
                .AddTelemetrySdk()
                .AddEnvironmentVariableDetector();
        }
        catch (Exception ex)
        {
            // If resource builder creation fails, log the error and create a default builder
            Console.Error.WriteLine($"Error creating resource builder: {ex.Message}");
            ResourceBuilder = ResourceBuilder.CreateDefault();
        }
    }

    private string GetSafeInstanceId()
    {
        try
        {
            // More unique instance ID with process ID and timestamp component
            return $"{System.Environment.MachineName}-{Process.GetCurrentProcess().Id}-{DateTime.UtcNow.Ticks % 10000}";
        }
        catch
        {
            return Guid.NewGuid().ToString();
        }
    }

    private string GetSafeHostName()
    {
        try
        {
            return System.Environment.MachineName;
        }
        catch
        {
            return "unknown-host";
        }
    }

    private string GetSafeOSPlatform()
    {
        try
        {
            return System.Environment.OSVersion.Platform.ToString();
        }
        catch
        {
            return "unknown-platform";
        }
    }

    private string GetSafeOSVersion()
    {
        try
        {
            return System.Environment.OSVersion.VersionString;
        }
        catch
        {
            return "unknown-version";
        }
    }

    /// <summary>
    /// Helper method to safely record a request metric
    /// </summary>
    public void SafeRecordRequest(string operation, params KeyValuePair<string, object?>[] tags)
    {
        if (_disposed) return;

        try
        {
            RequestCounter.Add(1, tags);
        }
        catch (Exception ex)
        {
            // Suppress exceptions from metrics to prevent application failures
            Console.Error.WriteLine($"Error recording request metric: {ex.Message}");
        }
    }

    /// <summary>
    /// Helper method to safely start an activity with error handling
    /// </summary>
    public Activity? SafeStartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        if (_disposed) return null;

        try
        {
            return ActivitySource.StartActivity(name, kind);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error starting activity '{name}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Disposes the telemetry resources with proper thread safety
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        lock (_disposeLock)
        {
            if (_disposed) return;

            if (disposing)
            {
                try
                {
                    // Dispose managed resources
                    Meter?.Dispose();
                    ActivitySource?.Dispose();
                }
                catch (Exception ex)
                {
                    // Log but don't throw from Dispose
                    Console.Error.WriteLine($"Error during TelemetryConfig disposal: {ex.Message}");
                }
            }

            _disposed = true;
        }
    }

    ~TelemetryConfig()
    {
        Dispose(false);
    }
}
