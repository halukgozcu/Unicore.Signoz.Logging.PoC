using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Serilog.Context;
using Unicore.Common.OpenTelemetry.Configurations;
using Unicore.Common.OpenTelemetry.Contexts;

namespace Unicore.Common.OpenTelemetry.Enrichments;

public class LogEnricher : BackgroundService
{
    private readonly TelemetryConfig _config;

    public LogEnricher(TelemetryConfig config)
    {
        _config = config;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Activity.Current = null;

        // Subscribe to Activity events to enrich logs with trace context
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                EnrichWithTraceInfo(activity);
                EnrichWithActivityTags(activity);
                EnrichWithExecutionContext(activity);
            },
            ActivityStopped = activity =>
            {
                EnrichWithTraceInfo(activity);
                EnrichWithActivityTags(activity);
                EnrichWithPerformanceMetrics(activity);
            }
        });

        return Task.CompletedTask;
    }

    private static void EnrichWithTraceInfo(Activity? activity)
    {
        if (activity == null) return;

        LogContext.PushProperty("TraceId", activity.TraceId.ToString());
        LogContext.PushProperty("SpanId", activity.SpanId.ToString());
        LogContext.PushProperty("ParentSpanId", activity.ParentSpanId.ToString());

        if (activity.Parent != null)
        {
            LogContext.PushProperty("ParentTraceId", activity.Parent.TraceId.ToString());
        }
    }

    private void EnrichWithActivityTags(Activity? activity)
    {
        if (activity == null) return;

        // Add standard W3C trace context
        LogContext.PushProperty("TraceFlags", activity.ActivityTraceFlags.ToString());
        LogContext.PushProperty("ActivitySource", activity.Source.Name);
        LogContext.PushProperty("ActivityName", activity.DisplayName);
        LogContext.PushProperty("ActivityKind", activity.Kind.ToString());

        // Add service context
        LogContext.PushProperty("ServiceName", _config.ServiceName);
        LogContext.PushProperty("ServiceNamespace", _config.ServiceNamespace);
        LogContext.PushProperty("ServiceVersion", _config.ServiceVersion);
        LogContext.PushProperty("DeploymentEnvironment", _config.Environment);
        LogContext.PushProperty("HostName", Environment.MachineName);
        LogContext.PushProperty("ProcessId", Environment.ProcessId);

        // Add any activity tags as properties
        foreach (var tag in activity.Tags)
        {
            LogContext.PushProperty($"tag_{tag.Key}", tag.Value);
        }

        // Add baggage items
        foreach (var baggage in activity.Baggage)
        {
            LogContext.PushProperty($"baggage_{baggage.Key}", baggage.Value);
        }

        // Add status information if present
        if (activity.Status != ActivityStatusCode.Unset)
        {
            LogContext.PushProperty("ActivityStatus", activity.Status.ToString());
            if (!string.IsNullOrEmpty(activity.StatusDescription))
            {
                LogContext.PushProperty("ActivityStatusDescription", activity.StatusDescription);
            }
        }

        // Add duration for completed activities
        if (activity.Duration != TimeSpan.Zero)
        {
            LogContext.PushProperty("ActivityDurationMs", activity.Duration.TotalMilliseconds);
        }
    }

    private void EnrichWithExecutionContext(Activity? activity)
    {
        if (activity == null) return;

        // Add thread and thread pool information
        LogContext.PushProperty("ThreadId", Thread.CurrentThread.ManagedThreadId);
        LogContext.PushProperty("IsThreadPoolThread", Thread.CurrentThread.IsThreadPoolThread);
        LogContext.PushProperty("IsBackground", Thread.CurrentThread.IsBackground);

        // Add process information
        var process = Process.GetCurrentProcess();
        LogContext.PushProperty("ProcessWorkingSet", process.WorkingSet64);
        LogContext.PushProperty("ProcessCpuTime", process.TotalProcessorTime.TotalMilliseconds);
        LogContext.PushProperty("ProcessThreadCount", process.Threads.Count);

        // Add timestamp with high precision
        LogContext.PushProperty("TimestampUtc", DateTime.UtcNow);
        LogContext.PushProperty("Unix", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        // Add activity creation context 
        if (activity.Source != null)
        {
            LogContext.PushProperty("ActivitySourceVersion", activity.Source.Version);
        }

        // Custom resource ID for cloud environments
        string resourceId = Environment.GetEnvironmentVariable("RESOURCE_ID") ??
                            Environment.GetEnvironmentVariable("WEBSITE_RESOURCE_ID") ??
                            Environment.MachineName;
        LogContext.PushProperty("ResourceId", resourceId);
    }

    private void EnrichWithPerformanceMetrics(Activity? activity)
    {
        if (activity == null || activity.Duration == TimeSpan.Zero) return;

        // Add duration with different precision formats
        LogContext.PushProperty("DurationMs", activity.Duration.TotalMilliseconds);
        LogContext.PushProperty("DurationSec", activity.Duration.TotalSeconds);

        // Add performance category based on duration
        string performanceCategory = activity.Duration.TotalMilliseconds switch
        {
            < 10 => "VeryFast",
            < 100 => "Fast",
            < 1000 => "Normal",
            < 3000 => "Slow",
            _ => "VerySlow"
        };
        LogContext.PushProperty("PerformanceCategory", performanceCategory);

        // Add time buckets for easier metrics aggregation
        if (activity.Duration.TotalMilliseconds < 10)
            LogContext.PushProperty("DurationBucket", "0-10ms");
        else if (activity.Duration.TotalMilliseconds < 100)
            LogContext.PushProperty("DurationBucket", "10-100ms");
        else if (activity.Duration.TotalMilliseconds < 500)
            LogContext.PushProperty("DurationBucket", "100-500ms");
        else if (activity.Duration.TotalMilliseconds < 1000)
            LogContext.PushProperty("DurationBucket", "500-1000ms");
        else if (activity.Duration.TotalMilliseconds < 3000)
            LogContext.PushProperty("DurationBucket", "1-3s");
        else if (activity.Duration.TotalMilliseconds < 10000)
            LogContext.PushProperty("DurationBucket", "3-10s");
        else
            LogContext.PushProperty("DurationBucket", "10s+");

        // Add timestamp of activity completion
        LogContext.PushProperty("CompletedAt", DateTime.UtcNow);
    }
}
