using System.Diagnostics;
using System.Collections.Generic;
using System.Text;

namespace Unicore.Common.OpenTelemetry.Tracing;

/// <summary>
/// Handles propagation of trace context for message brokers like RabbitMQ and Kafka
/// </summary>
public static class MessageBrokerTraceContext
{
    private const string TraceParentHeader = "traceparent";
    private const string TraceStateHeader = "tracestate";
    private const string BaggageHeader = "baggage";

    /// <summary>
    /// Injects the current trace context into message headers (for producers)
    /// </summary>
    /// <returns>Dictionary of headers to add to the outgoing message</returns>
    public static Dictionary<string, string> InjectTraceContext()
    {
        var headers = new Dictionary<string, string>();

        // Get current activity (if any)
        var activity = Activity.Current;
        if (activity == null)
            return headers;

        // Add W3C traceparent header
        headers[TraceParentHeader] = activity.Id;

        // Add tracestate if available
        if (!string.IsNullOrEmpty(activity.TraceStateString))
        {
            headers[TraceStateHeader] = activity.TraceStateString;
        }

        // Add baggage items if any exist
        if (activity.Baggage.Any())
        {
            var baggageBuilder = new StringBuilder();
            foreach (var (key, value) in activity.Baggage)
            {
                if (baggageBuilder.Length > 0)
                    baggageBuilder.Append(',');
                baggageBuilder.Append(key).Append('=').Append(Uri.EscapeDataString(value));
            }
            headers[BaggageHeader] = baggageBuilder.ToString();
        }

        return headers;
    }

    /// <summary>
    /// Extracts trace context from message headers and creates a linked activity (for consumers)
    /// </summary>
    /// <param name="headers">Headers from the received message</param>
    /// <param name="operationName">Name for the new activity</param>
    /// <param name="kind">Kind of activity (typically Consumer)</param>
    /// <param name="sourceSystem">Source system name (e.g., "RabbitMQ" or "Kafka")</param>
    /// <param name="baseAttributes">Additional base attributes for the activity</param>
    /// <returns>A new activity linked to the parent trace or null if trace context couldn't be extracted</returns>
    public static Activity? ExtractTraceContext(
        IDictionary<string, string> headers,
        string operationName,
        ActivityKind kind,
        string sourceSystem,
        Dictionary<string, object?>? baseAttributes = null)
    {
        // Create activity source specific to the broker type
        var source = new ActivitySource($"{sourceSystem}.Consumer");

        // First check if trace context exists in headers
        if (!headers.TryGetValue(TraceParentHeader, out var traceparent))
        {
            // No trace context found, create new root activity
            return CreateActivity(source, operationName, kind, null, baseAttributes);
        }

        try
        {
            // Parse the traceparent to get the trace context
            ActivityContext parentContext;

            if (ActivityContext.TryParse(traceparent,
                headers.TryGetValue(TraceStateHeader, out var tracestate) ? tracestate : null,
                out parentContext))
            {
                // Create activity with the extracted parent context
                var activity = CreateActivity(source, operationName, kind, parentContext, baseAttributes);

                // Process baggage if available
                if (headers.TryGetValue(BaggageHeader, out var baggage) && activity != null)
                {
                    foreach (var baggageItem in ParseBaggage(baggage))
                    {
                        activity.AddBaggage(baggageItem.Key, baggageItem.Value);
                    }
                }

                return activity;
            }
        }
        catch (Exception)
        {
            // If context parsing fails, fall back to creating a new root activity
        }

        // Fallback: create new root activity
        return CreateActivity(source, operationName, kind, null, baseAttributes);
    }

    private static Activity? CreateActivity(
        ActivitySource source,
        string operationName,
        ActivityKind kind,
        ActivityContext? parentContext,
        Dictionary<string, object?>? attributes)
    {
        Activity? activity;

        if (parentContext.HasValue)
        {
            // Create activity with parent context
            activity = source.StartActivity(operationName, kind, parentContext.Value);
        }
        else
        {
            // Create new root activity
            activity = source.StartActivity(operationName, kind);
        }

        // Add attributes if any
        if (activity != null && attributes != null)
        {
            foreach (var attribute in attributes)
            {
                if (attribute.Key != null)
                {
                    activity.SetTag(attribute.Key, attribute.Value);
                }
            }
        }

        return activity;
    }

    private static IEnumerable<KeyValuePair<string, string>> ParseBaggage(string baggage)
    {
        if (string.IsNullOrEmpty(baggage))
            yield break;

        foreach (var item in baggage.Split(','))
        {
            var parts = item.Split('=');
            if (parts.Length == 2)
            {
                yield return new KeyValuePair<string, string>(
                    parts[0].Trim(),
                    Uri.UnescapeDataString(parts[1].Trim()));
            }
        }
    }
}
