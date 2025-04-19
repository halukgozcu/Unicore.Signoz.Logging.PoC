using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;
using Unicore.Common.OpenTelemetry.Tracing;

namespace Unicore.Common.OpenTelemetry.Enrichments;

/// <summary>
/// Enriches logs with RabbitMQ message context information
/// </summary>
public class RabbitMQEnricher : ILogEventEnricher
{
    // Thread-local storage for RabbitMQ message context
    private static readonly AsyncLocal<RabbitMQMessageContext> CurrentContext = new AsyncLocal<RabbitMQMessageContext>();

    /// <summary>
    /// Creates a disposable scope for RabbitMQ message processing
    /// </summary>
    /// <param name="exchange">Exchange name</param>
    /// <param name="routingKey">Routing key</param>
    /// <param name="queue">Queue name</param>
    /// <param name="deliveryTag">Delivery tag</param>
    /// <param name="messageId">Optional message ID</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <param name="consumerTag">Optional consumer tag</param>
    /// <returns>A disposable scope that automatically cleans up when disposed</returns>
    public static IDisposable CreateScope(
        string exchange,
        string routingKey,
        string queue,
        ulong deliveryTag,
        string? messageId = null,
        string? correlationId = null,
        string? consumerTag = null)
    {
        return new RabbitMQMessageScope(exchange, routingKey, queue, deliveryTag, messageId, correlationId, consumerTag);
    }

    /// <summary>
    /// Extracts trace context from RabbitMQ message headers and creates a scope for processing
    /// </summary>
    /// <param name="exchange">Exchange name</param>
    /// <param name="routingKey">Routing key</param>
    /// <param name="queue">Queue name</param>
    /// <param name="deliveryTag">Delivery tag</param>
    /// <param name="headers">Message headers containing trace context</param>
    /// <param name="messageId">Optional message ID</param>
    /// <param name="correlationId">Optional correlation ID</param>
    /// <param name="consumerTag">Optional consumer tag</param>
    /// <returns>A disposable scope that automatically cleans up when disposed</returns>
    public static IDisposable CreateScope(
        string exchange,
        string routingKey,
        string queue,
        ulong deliveryTag,
        IDictionary<string, string> headers,
        string? messageId = null,
        string? correlationId = null,
        string? consumerTag = null)
    {
        return new RabbitMQMessageScope(
            exchange,
            routingKey,
            queue,
            deliveryTag,
            headers,
            messageId,
            correlationId,
            consumerTag);
    }

    /// <summary>
    /// Injects the current trace context into message headers for publishing
    /// </summary>
    /// <returns>Dictionary of headers to add to the outgoing message</returns>
    public static Dictionary<string, string> InjectTraceContext()
    {
        return MessageBrokerTraceContext.InjectTraceContext();
    }

    /// <summary>
    /// Sets the current RabbitMQ message context for the current execution flow
    /// </summary>
    public static void SetMessageContext(
        string exchange,
        string routingKey,
        string queue,
        ulong deliveryTag,
        string? messageId = null,
        string? correlationId = null,
        string? consumerTag = null)
    {
        CurrentContext.Value = new RabbitMQMessageContext
        {
            Exchange = exchange,
            RoutingKey = routingKey,
            Queue = queue,
            DeliveryTag = deliveryTag,
            MessageId = messageId,
            CorrelationId = correlationId,
            ConsumerTag = consumerTag,
            ReceivedAt = DateTimeOffset.UtcNow
        };

        // If there's an active Activity (trace), add the RabbitMQ context as tags
        if (Activity.Current != null)
        {
            Activity.Current.SetTag("messaging.system", "rabbitmq");
            Activity.Current.SetTag("messaging.destination", exchange);
            Activity.Current.SetTag("messaging.destination_kind", "exchange");
            Activity.Current.SetTag("messaging.rabbitmq.routing_key", routingKey);
            Activity.Current.SetTag("messaging.rabbitmq.queue", queue);
            Activity.Current.SetTag("messaging.rabbitmq.delivery_tag", deliveryTag);

            if (!string.IsNullOrEmpty(messageId))
            {
                Activity.Current.SetTag("messaging.message_id", messageId);
            }

            if (!string.IsNullOrEmpty(correlationId))
            {
                Activity.Current.SetTag("messaging.conversation_id", correlationId);
            }

            if (!string.IsNullOrEmpty(consumerTag))
            {
                Activity.Current.SetTag("messaging.rabbitmq.consumer_tag", consumerTag);
            }
        }
    }

    /// <summary>
    /// Clears the current RabbitMQ message context
    /// </summary>
    public static void ClearMessageContext()
    {
        CurrentContext.Value = null;
    }

    /// <summary>
    /// Creates a new Activity for processing a RabbitMQ message
    /// </summary>
    public static Activity? StartRabbitMQActivity(
        string operationName,
        string exchange,
        string routingKey,
        string queue,
        ulong deliveryTag)
    {
        var activitySource = new ActivitySource("RabbitMQ.Consumer");
        var activity = activitySource.StartActivity(
            operationName,
            ActivityKind.Consumer);

        if (activity != null)
        {
            // Set standard OpenTelemetry messaging attributes
            // https://github.com/open-telemetry/opentelemetry-specification/blob/main/specification/trace/semantic_conventions/messaging.md
            activity.SetTag("messaging.system", "rabbitmq");
            activity.SetTag("messaging.destination", exchange);
            activity.SetTag("messaging.destination_kind", "exchange");
            activity.SetTag("messaging.rabbitmq.routing_key", routingKey);
            activity.SetTag("messaging.rabbitmq.queue", queue);
            activity.SetTag("messaging.rabbitmq.delivery_tag", deliveryTag);
            activity.SetTag("messaging.operation", "process");
        }

        return activity;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        try
        {
            var context = CurrentContext.Value;
            if (context == null)
            {
                return;
            }

            // Add standard RabbitMQ message properties
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RabbitMQExchange", context.Exchange));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RabbitMQRoutingKey", context.RoutingKey));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RabbitMQQueue", context.Queue));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RabbitMQDeliveryTag", context.DeliveryTag));

            // Add optional properties
            if (!string.IsNullOrEmpty(context.MessageId))
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RabbitMQMessageId", context.MessageId));
            }

            if (!string.IsNullOrEmpty(context.CorrelationId))
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RabbitMQCorrelationId", context.CorrelationId));
            }

            if (!string.IsNullOrEmpty(context.ConsumerTag))
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RabbitMQConsumerTag", context.ConsumerTag));
            }

            // Add timing information
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RabbitMQMessageReceivedAt", context.ReceivedAt));

            var processingLatency = DateTimeOffset.UtcNow - context.ReceivedAt;
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RabbitMQProcessingLatencyMs", processingLatency.TotalMilliseconds));

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
        catch (Exception ex)
        {
            // Prevent enricher errors from breaking the application
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RabbitMQEnricherError", ex.Message));
        }
    }

    /// <summary>
    /// Context information for a RabbitMQ message
    /// </summary>
    private class RabbitMQMessageContext
    {
        public string Exchange { get; set; }
        public string RoutingKey { get; set; }
        public string Queue { get; set; }
        public ulong DeliveryTag { get; set; }
        public string MessageId { get; set; }
        public string CorrelationId { get; set; }
        public string ConsumerTag { get; set; }
        public DateTimeOffset ReceivedAt { get; set; }

        // Add trace context fields
        public string? TraceId { get; set; }
        public string? SpanId { get; set; }
        public string? ParentSpanId { get; set; }
    }

    /// <summary>
    /// Disposable scope for RabbitMQ message context
    /// </summary>
    private class RabbitMQMessageScope : IDisposable
    {
        private readonly Activity? _createdActivity;
        private bool _disposed;

        public RabbitMQMessageScope(
            string exchange,
            string routingKey,
            string queue,
            ulong deliveryTag,
            string? messageId,
            string? correlationId,
            string? consumerTag)
        {
            // Set the message context
            SetMessageContext(exchange, routingKey, queue, deliveryTag, messageId, correlationId, consumerTag);

            // Create an activity if none exists
            if (Activity.Current == null)
            {
                _createdActivity = StartRabbitMQActivity(
                    $"Process {exchange}/{routingKey}",
                    exchange,
                    routingKey,
                    queue,
                    deliveryTag);
            }
            else
            {
                _createdActivity = Activity.Current;
            }
        }

        public RabbitMQMessageScope(
            string exchange,
            string routingKey,
            string queue,
            ulong deliveryTag,
            IDictionary<string, string> headers,
            string? messageId,
            string? correlationId,
            string? consumerTag)
        {
            // Set the message context
            SetMessageContext(exchange, routingKey, queue, deliveryTag, messageId, correlationId, consumerTag);

            // Create a new activity with the extracted trace context
            _createdActivity = MessageBrokerTraceContext.ExtractTraceContext(
                headers,
                $"Process {exchange}/{routingKey}",
                ActivityKind.Consumer,
                "RabbitMQ",
                new Dictionary<string, object?>
                {
                    ["messaging.system"] = "rabbitmq",
                    ["messaging.destination"] = exchange,
                    ["messaging.destination_kind"] = "exchange",
                    ["messaging.rabbitmq.routing_key"] = routingKey,
                    ["messaging.rabbitmq.queue"] = queue,
                    ["messaging.rabbitmq.delivery_tag"] = deliveryTag,
                    ["messaging.operation"] = "process"
                });

            // Set additional tags if we have message or correlation IDs
            if (_createdActivity != null)
            {
                if (!string.IsNullOrEmpty(messageId))
                {
                    _createdActivity.SetTag("messaging.message_id", messageId);
                }

                if (!string.IsNullOrEmpty(correlationId))
                {
                    _createdActivity.SetTag("messaging.conversation_id", correlationId);
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
                // Clear the context
                ClearMessageContext();

                // Dispose the activity we created
                _createdActivity?.Dispose();
            }
            catch (Exception)
            {
                // Suppressing exceptions during disposal to avoid crashing the application
            }

            _disposed = true;
        }
    }
}
