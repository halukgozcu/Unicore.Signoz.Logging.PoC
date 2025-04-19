using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;
using Unicore.Common.OpenTelemetry.Tracing;

namespace Unicore.Common.OpenTelemetry.Enrichments;

/// <summary>
/// Enriches logs with Kafka consumer context information
/// </summary>
public class KafkaConsumerEnricher : ILogEventEnricher
{
    // Thread-local storage for Kafka message context
    private static readonly AsyncLocal<KafkaMessageContext> CurrentContext = new AsyncLocal<KafkaMessageContext>();

    /// <summary>
    /// Creates a disposable scope for Kafka message context
    /// </summary>
    /// <param name="topic">Kafka topic name</param>
    /// <param name="partition">Partition number</param>
    /// <param name="offset">Message offset in partition</param>
    /// <param name="headers">Message headers for trace context extraction</param>
    /// <param name="key">Optional message key</param>
    /// <param name="groupId">Optional consumer group ID</param>
    /// <returns>A disposable scope that automatically clears the context when disposed</returns>
    public static IDisposable CreateScope(
        string topic,
        int partition,
        long offset,
        IDictionary<string, string> headers,
        string? key = null,
        string? groupId = null)
    {
        return new KafkaMessageScope(topic, partition, offset, headers, key, groupId);
    }

    /// <summary>
    /// Injects the current trace context into message headers for Kafka producers
    /// </summary>
    /// <returns>Dictionary of headers to add to outgoing Kafka messages</returns>
    public static Dictionary<string, string> InjectTraceContext()
    {
        return MessageBrokerTraceContext.InjectTraceContext();
    }

    public static void SetMessageContext(string topic, int partition, long offset, string? key = null, string? groupId = null)
    {
        CurrentContext.Value = new KafkaMessageContext
        {
            Topic = topic,
            Partition = partition,
            Offset = offset,
            Key = key,
            GroupId = groupId,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        // If there's an active Activity (trace), add the Kafka context as tags
        if (Activity.Current != null)
        {
            Activity.Current.SetTag("messaging.system", "kafka");
            Activity.Current.SetTag("messaging.destination", topic);
            Activity.Current.SetTag("messaging.destination_kind", "topic");
            Activity.Current.SetTag("messaging.kafka.partition", partition);
            Activity.Current.SetTag("messaging.kafka.offset", offset);

            if (!string.IsNullOrEmpty(key))
            {
                Activity.Current.SetTag("messaging.kafka.message_key", key);
            }

            if (!string.IsNullOrEmpty(groupId))
            {
                Activity.Current.SetTag("messaging.kafka.consumer_group", groupId);
            }
        }
    }

    public static void ClearMessageContext()
    {
        CurrentContext.Value = null;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var context = CurrentContext.Value;
        if (context == null)
        {
            return;
        }

        // Add standard Kafka message properties
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("KafkaTopic", context.Topic));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("KafkaPartition", context.Partition));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("KafkaOffset", context.Offset));

        // Add optional properties
        if (!string.IsNullOrEmpty(context.Key))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("KafkaMessageKey", context.Key));
        }

        if (!string.IsNullOrEmpty(context.GroupId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("KafkaConsumerGroup", context.GroupId));
        }

        // Add timing information
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("KafkaMessageReceivedAt", context.ReceivedAt));

        var processingLatency = DateTimeOffset.UtcNow - context.ReceivedAt;
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("KafkaProcessingLatencyMs", processingLatency.TotalMilliseconds));

        // Add trace context IDs
        if (Activity.Current != null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", Activity.Current.TraceId.ToString()));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", Activity.Current.SpanId.ToString()));

            if (Activity.Current.ParentSpanId != default)
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ParentSpanId",
                    Activity.Current.ParentSpanId.ToString()));
            }
        }
    }

    private class KafkaMessageContext
    {
        public string Topic { get; set; }
        public int Partition { get; set; }
        public long Offset { get; set; }
        public string Key { get; set; }
        public string GroupId { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }

        // Add trace context fields
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
        public string? ParentSpanId { get; set; }
    }

    /// <summary>
    /// Disposable scope for Kafka message context
    /// </summary>
    private class KafkaMessageScope : IDisposable
    {
        private readonly Activity? _createdActivity;
        private bool _disposed;

        public KafkaMessageScope(
            string topic,
            int partition,
            long offset,
            IDictionary<string, string> headers,
            string? key,
            string? groupId)
        {
            // Set message context
            SetMessageContext(topic, partition, offset, key, groupId);

            // Extract trace context and create activity
            _createdActivity = MessageBrokerTraceContext.ExtractTraceContext(
                headers,
                $"Process {topic}",
                ActivityKind.Consumer,
                "Kafka",
                new Dictionary<string, object?>
                {
                    ["messaging.system"] = "kafka",
                    ["messaging.destination"] = topic,
                    ["messaging.destination_kind"] = "topic",
                    ["messaging.kafka.partition"] = partition,
                    ["messaging.kafka.offset"] = offset,
                    ["messaging.operation"] = "process"
                });

            // Add additional tags
            if (_createdActivity != null)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    _createdActivity.SetTag("messaging.kafka.message_key", key);
                }

                if (!string.IsNullOrEmpty(groupId))
                {
                    _createdActivity.SetTag("messaging.kafka.consumer_group", groupId);
                }
            }

            // Update the context with trace IDs
            if (_createdActivity != null && CurrentContext.Value != null)
            {
                CurrentContext.Value.TraceId = _createdActivity.TraceId.ToString();
                CurrentContext.Value.SpanId = _createdActivity.SpanId.ToString();
                CurrentContext.Value.ParentSpanId = _createdActivity.ParentSpanId.ToString();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                // Clear the Kafka message context
                ClearMessageContext();

                // Dispose the activity
                _createdActivity?.Dispose();
            }
            catch (Exception)
            {
                // Suppressing exceptions during disposal
            }

            _disposed = true;
        }
    }
}
