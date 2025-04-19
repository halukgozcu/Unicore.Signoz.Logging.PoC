using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;
using Unicore.Common.OpenTelemetry.Configurations;

namespace Unicore.Common.OpenTelemetry.Enrichments;

/// <summary>
/// Enriches logs with trace context information
/// </summary>
public class TraceEnricher : BaseEnricher
{
    private readonly TelemetryConfig _config;

    public TraceEnricher(TelemetryConfig config)
    {
        _config = config;
    }

    protected override void EnrichInternal(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity == null) return;

        // Add trace identifiers
        AddPropertyIfNotNull(logEvent, propertyFactory, "TraceId", activity.TraceId.ToString());
        AddPropertyIfNotNull(logEvent, propertyFactory, "SpanId", activity.SpanId.ToString());
        AddPropertyIfNotNull(logEvent, propertyFactory, "ParentSpanId", activity.ParentSpanId.ToString());

        // Add activity details
        AddPropertyIfNotNull(logEvent, propertyFactory, "ActivityName", activity.DisplayName);
        AddPropertyIfNotNull(logEvent, propertyFactory, "ActivitySource", activity.Source.Name);
        AddPropertyIfNotNull(logEvent, propertyFactory, "ActivityKind", activity.Kind.ToString());

        // Add service information
        AddPropertyIfNotNull(logEvent, propertyFactory, "ServiceName", _config.ServiceName);
        AddPropertyIfNotNull(logEvent, propertyFactory, "ServiceVersion", _config.ServiceVersion);
        AddPropertyIfNotNull(logEvent, propertyFactory, "Environment", _config.Environment);

        // Add timing information if available
        if (activity.Duration != TimeSpan.Zero)
        {
            AddPropertyIfNotNull(logEvent, propertyFactory, "DurationMs", activity.Duration.TotalMilliseconds);
        }

        // Add relevant activity tags
        foreach (var tag in activity.Tags)
        {
            // Don't duplicate already set properties
            if (!tag.Key.Contains('.') && !tag.Key.Contains(':') && tag.Value != null)
            {
                AddPropertyIfNotNull(logEvent, propertyFactory, tag.Key, tag.Value);
            }
        }
    }
}
