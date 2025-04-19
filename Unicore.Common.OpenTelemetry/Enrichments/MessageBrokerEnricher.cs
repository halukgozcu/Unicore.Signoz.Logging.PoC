using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;
using Unicore.Common.OpenTelemetry.Contexts;
using Unicore.Common.OpenTelemetry.Models;

namespace Unicore.Common.OpenTelemetry.Enrichments;

/// <summary>
/// Unified message broker enricher for Kafka and RabbitMQ
/// </summary>
public class MessageBrokerEnricher : BaseEnricher
{
    // Keys used in the Telemetry Context
    private const string KafkaContextKey = "KafkaContext";
    private const string RabbitContextKey = "RabbitContext";

    /// <summary>
    /// Sets Kafka context for the current execution flow
    /// </summary>
    public static void SetKafkaContext(string topic, int partition, long offset,
        string key = null, string groupId = null)
    {
        var context = new KafkaContext
        {
            Topic = topic,
            Partition = partition,
            Offset = offset,
            Key = key,
            GroupId = groupId,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        TelemetryContext.Set(KafkaContextKey, context);

        // Set standard tags on the current activity
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

    /// <summary>
    /// Sets RabbitMQ context for the current execution flow
    /// </summary>
    public static void SetRabbitMQContext(string exchange, string routingKey, string queue,
        ulong deliveryTag, string messageId = null, string correlationId = null)
    {
        var context = new RabbitMQContext
        {
            Exchange = exchange,
            RoutingKey = routingKey,
            Queue = queue,
            DeliveryTag = deliveryTag,
            MessageId = messageId,
            CorrelationId = correlationId,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        TelemetryContext.Set(RabbitContextKey, context);

        // Set standard tags on the current activity
        if (Activity.Current != null)
        {
            Activity.Current.SetTag("messaging.system", "rabbitmq");
            Activity.Current.SetTag("messaging.destination", exchange);
            Activity.Current.SetTag("messaging.destination_kind", "exchange");
            Activity.Current.SetTag("messaging.rabbitmq.routing_key", routingKey);
            Activity.Current.SetTag("messaging.rabbitmq.queue", queue);

            if (!string.IsNullOrEmpty(messageId))
            {
                Activity.Current.SetTag("messaging.message_id", messageId);
            }

            if (!string.IsNullOrEmpty(correlationId))
            {
                Activity.Current.SetTag("messaging.correlation_id", correlationId);
            }
        }
    }

    /// <summary>
    /// Clears all message broker contexts
    /// </summary>
    public static void ClearMessageContext()
    {
        TelemetryContext.Remove(KafkaContextKey);
        TelemetryContext.Remove(RabbitContextKey);
    }

    /// <summary>
    /// Creates an activity for processing a message from any broker
    /// </summary>
    public static Activity StartMessageProcessingActivity(string name, string system, string destination)
    {
        var source = new ActivitySource($"MessageBroker.{system}");
        var activity = source.StartActivity(name, ActivityKind.Consumer);

        if (activity != null)
        {
            activity.SetTag("messaging.system", system.ToLowerInvariant());
            activity.SetTag("messaging.destination", destination);
            activity.SetTag("messaging.operation", "process");
        }

        return activity;
    }

    protected override void EnrichInternal(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Try to get the Kafka context first
        var kafkaContext = TelemetryContext.Get<KafkaContext>(KafkaContextKey);
        if (kafkaContext != null)
        {
            EnrichWithKafkaContext(logEvent, propertyFactory, kafkaContext);
            return;
        }

        // Try RabbitMQ context next
        var rabbitContext = TelemetryContext.Get<RabbitMQContext>(RabbitContextKey);
        if (rabbitContext != null)
        {
            EnrichWithRabbitMQContext(logEvent, propertyFactory, rabbitContext);
        }
    }

    private void EnrichWithKafkaContext(LogEvent logEvent, ILogEventPropertyFactory factory, KafkaContext context)
    {
        // Add basic Kafka properties
        AddPropertyIfNotNull(logEvent, factory, "KafkaTopic", context.Topic);
        AddPropertyIfNotNull(logEvent, factory, "KafkaPartition", context.Partition);
        AddPropertyIfNotNull(logEvent, factory, "KafkaOffset", context.Offset);

        // Add optional properties
        AddPropertyIfNotNull(logEvent, factory, "KafkaMessageKey", context.Key);
        AddPropertyIfNotNull(logEvent, factory, "KafkaConsumerGroup", context.GroupId);

        // Add timing information
        AddPropertyIfNotNull(logEvent, factory, "KafkaMessageReceivedAt", context.ReceivedAt);

        var processingTime = DateTimeOffset.UtcNow - context.ReceivedAt;
        AddPropertyIfNotNull(logEvent, factory, "KafkaProcessingTimeMs", processingTime.TotalMilliseconds);
    }

    private void EnrichWithRabbitMQContext(LogEvent logEvent, ILogEventPropertyFactory factory, RabbitMQContext context)
    {
        // Add basic RabbitMQ properties
        AddPropertyIfNotNull(logEvent, factory, "RabbitMQExchange", context.Exchange);
        AddPropertyIfNotNull(logEvent, factory, "RabbitMQRoutingKey", context.RoutingKey);
        AddPropertyIfNotNull(logEvent, factory, "RabbitMQQueue", context.Queue);
        AddPropertyIfNotNull(logEvent, factory, "RabbitMQDeliveryTag", context.DeliveryTag);

        // Add optional properties
        AddPropertyIfNotNull(logEvent, factory, "RabbitMQMessageId", context.MessageId);
        AddPropertyIfNotNull(logEvent, factory, "RabbitMQCorrelationId", context.CorrelationId);

        // Add timing information
        AddPropertyIfNotNull(logEvent, factory, "RabbitMQMessageReceivedAt", context.ReceivedAt);

        var processingTime = DateTimeOffset.UtcNow - context.ReceivedAt;
        AddPropertyIfNotNull(logEvent, factory, "RabbitMQProcessingTimeMs", processingTime.TotalMilliseconds);
    }
}
