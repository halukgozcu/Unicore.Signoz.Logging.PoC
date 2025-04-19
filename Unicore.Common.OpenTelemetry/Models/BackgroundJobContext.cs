namespace Unicore.Common.OpenTelemetry.Models;

/// <summary>
/// Context information for background jobs
/// </summary>
public class BackgroundJobContext
{
    public string JobId { get; set; }
    public string JobName { get; set; }
    public string Queue { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime StartedAt { get; set; }
}
