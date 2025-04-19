namespace Unicore.Common.OpenTelemetry.Models;

/// <summary>
/// Context information for a Kafka message
/// </summary>
public class KafkaContext
{
    public string Topic { get; set; }
    public int Partition { get; set; }
    public long Offset { get; set; }
    public string Key { get; set; }
    public string GroupId { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
