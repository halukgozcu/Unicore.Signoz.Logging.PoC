using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Serilog.Context;
using Unicore.Common.OpenTelemetry.Configuration;

namespace Unicore.Common.OpenTelemetry.Services;

/// <summary>
/// Helper class to enrich logs with trace context
/// </summary>
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

        // Subscribe to the ActivityStarted event
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (activity != null)
                {
                    // Push trace and span IDs into the LogContext for Serilog
                    LogContext.PushProperty("TraceId", activity.TraceId.ToString());
                    LogContext.PushProperty("SpanId", activity.SpanId.ToString());
                    LogContext.PushProperty("ParentSpanId", activity.ParentSpanId.ToString());
                    LogContext.PushProperty("ServiceName", _config.ServiceName);

                    if (activity.Kind != ActivityKind.Internal)
                    {
                        LogContext.PushProperty("SpanKind", activity.Kind.ToString());
                    }

                    if (!string.IsNullOrEmpty(activity.DisplayName))
                    {
                        LogContext.PushProperty("ActivityName", activity.DisplayName);
                    }

                    if (activity.Tags.Any())
                    {
                        foreach (var tag in activity.Tags)
                        {
                            LogContext.PushProperty($"Tag_{tag.Key}", tag.Value);
                        }
                    }
                }
            },
            ActivityStopped = activity =>
            {
                // Clean up context when activity stops
                LogContext.Reset();
            }
        });

        return Task.CompletedTask;
    }
}
