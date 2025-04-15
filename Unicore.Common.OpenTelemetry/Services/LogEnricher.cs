using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Serilog.Context;
using Unicore.Common.OpenTelemetry.Configuration;

namespace Unicore.Common.OpenTelemetry.Services;

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
            },
            ActivityStopped = activity =>
            {
                EnrichWithTraceInfo(activity);
                EnrichWithActivityTags(activity);
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
}
