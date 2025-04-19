using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;
using Unicore.Common.OpenTelemetry.Contexts;
using Unicore.Common.OpenTelemetry.Models;

namespace Unicore.Common.OpenTelemetry.Enrichments;

/// <summary>
/// Enriches logs with background job context information
/// </summary>
public class BackgroundJobEnricher : BaseEnricher
{
    private const string JobContextKey = "BackgroundJobContext";

    /// <summary>
    /// Creates a disposable scope for a background job context
    /// </summary>
    /// <param name="jobId">Unique identifier for this job</param>
    /// <param name="jobName">Name of the job being executed</param>
    /// <param name="queue">Queue name for the job (defaults to "default")</param>
    /// <param name="createdAt">When the job was created (defaults to current time)</param>
    /// <returns>A disposable job context scope</returns>
    public static IDisposable CreateScope(string jobId, string jobName, string queue = "default", DateTime? createdAt = null)
    {
        return new BackgroundJobScope(jobId, jobName, queue, createdAt);
    }

    /// <summary>
    /// Sets job context for the current execution flow - consider using CreateScope instead for automatic cleanup
    /// </summary>
    public static void SetJobContext(string jobId, string jobName, string queue = "default", DateTime? createdAt = null)
    {
        var context = new BackgroundJobContext
        {
            JobId = jobId,
            JobName = jobName,
            Queue = queue,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            StartedAt = DateTime.UtcNow
        };

        TelemetryContext.Set(JobContextKey, context);

        // Create activity if none exists
        if (Activity.Current == null)
        {
            var source = new ActivitySource("BackgroundJobs");
            var activity = source.StartActivity($"Job.{jobName}", ActivityKind.Internal);

            if (activity != null)
            {
                activity.SetTag("job.id", jobId);
                activity.SetTag("job.name", jobName);
                activity.SetTag("job.queue", queue);

                if (createdAt.HasValue)
                {
                    var queueTime = DateTime.UtcNow - createdAt.Value;
                    activity.SetTag("job.queue_time_ms", queueTime.TotalMilliseconds);
                }
            }
        }
        // Or add tags to existing activity
        else if (Activity.Current != null)
        {
            Activity.Current.SetTag("job.id", jobId);
            Activity.Current.SetTag("job.name", jobName);
            Activity.Current.SetTag("job.queue", queue);

            if (createdAt.HasValue)
            {
                var queueTime = DateTime.UtcNow - createdAt.Value;
                Activity.Current.SetTag("job.queue_time_ms", queueTime.TotalMilliseconds);
            }
        }
    }

    /// <summary>
    /// Clears the job context - consider using CreateScope instead for automatic cleanup
    /// </summary>
    public static void ClearJobContext()
    {
        TelemetryContext.Remove(JobContextKey);
    }

    protected override void EnrichInternal(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var context = TelemetryContext.Get<BackgroundJobContext>(JobContextKey);
        if (context == null)
        {
            return;
        }

        // Add job properties
        AddPropertyIfNotNull(logEvent, propertyFactory, "JobId", context.JobId);
        AddPropertyIfNotNull(logEvent, propertyFactory, "JobName", context.JobName);
        AddPropertyIfNotNull(logEvent, propertyFactory, "JobQueue", context.Queue);

        // Add timing information
        AddPropertyIfNotNull(logEvent, propertyFactory, "JobCreatedAt", context.CreatedAt);
        AddPropertyIfNotNull(logEvent, propertyFactory, "JobStartedAt", context.StartedAt);

        // Calculate queue time
        if (context.CreatedAt != default)
        {
            var queueTime = context.StartedAt - context.CreatedAt;
            AddPropertyIfNotNull(logEvent, propertyFactory, "JobQueueTimeMs", queueTime.TotalMilliseconds);
        }

        // Calculate execution time
        var executionTime = DateTime.UtcNow - context.StartedAt;
        AddPropertyIfNotNull(logEvent, propertyFactory, "JobExecutionTimeMs", executionTime.TotalMilliseconds);
    }

    /// <summary>
    /// Disposable scope for background job context
    /// </summary>
    private class BackgroundJobScope : IDisposable
    {
        private readonly Activity _createdActivity;
        private bool _disposed;

        public BackgroundJobScope(string jobId, string jobName, string queue = "default", DateTime? createdAt = null)
        {
            // Set the job context
            SetJobContext(jobId, jobName, queue, createdAt);

            // Track if we created a new activity so we can dispose it properly
            _createdActivity = Activity.Current;
        }

        public void Dispose()
        {
            if (_disposed) return;

            // Clear the job context
            ClearJobContext();

            // Dispose the activity if we created it
            if (_createdActivity != null && _createdActivity == Activity.Current)
            {
                _createdActivity.Dispose();
            }

            _disposed = true;
        }
    }
}
