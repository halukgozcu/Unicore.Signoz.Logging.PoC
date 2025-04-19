namespace Unicore.Common.OpenTelemetry.Models;

/// <summary>
/// Context information for a RabbitMQ message
/// </summary>
public class RabbitMQContext
{
    public string Exchange { get; set; }
    public string RoutingKey { get; set; }
    public string Queue { get; set; }
    public ulong DeliveryTag { get; set; }
    public string MessageId { get; set; }
    public string CorrelationId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
