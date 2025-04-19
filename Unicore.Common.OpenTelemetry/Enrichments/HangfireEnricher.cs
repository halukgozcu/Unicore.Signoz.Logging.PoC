using System.Diagnostics;
using System.Reflection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Unicore.Common.OpenTelemetry.Enrichments;

/// <summary>
/// Enriches logs with Hangfire job context information
/// </summary>
public class HangfireEnricher : ILogEventEnricher
{
    private static readonly AssemblyName HangfireAssembly = new AssemblyName("Hangfire.Core");
    private static readonly object _lockObject = new object(); // For thread safety
    private static bool _hangfireAvailabilityChecked = false;
    private static bool _isHangfireAvailable = false;

    /// <summary>
    /// Creates a disposable scope for Hangfire job context
    /// </summary>
    /// <returns>A disposable context scope that automatically cleans up the context when disposed</returns>
    public static IDisposable CreateScope()
    {
        return new HangfireScope();
    }

    /// <summary>
    /// Creates a disposable scope for a specific Hangfire job
    /// </summary>
    /// <param name="jobId">Hangfire job ID</param>
    /// <param name="jobName">Name of the job method</param>
    /// <param name="queue">Queue name (default: "default")</param>
    /// <param name="createdAt">When the job was created (default: now)</param>
    /// <returns>A disposable context scope for the job</returns>
    public static IDisposable CreateJobScope(string jobId, string jobName, string queue = "default", DateTime? createdAt = null)
    {
        return new HangfireJobScope(jobId, jobName, queue, createdAt ?? DateTime.UtcNow);
    }

    /// <summary>
    /// Triggers a job event with the given name and adds it to the current Activity
    /// </summary>
    /// <param name="eventName">Name of the event</param>
    /// <param name="attributes">Optional event attributes</param>
    public static void TriggerJobEvent(string eventName, Dictionary<string, object> attributes = null)
    {
        if (Activity.Current == null)
            return;

        var tagsCollection = new ActivityTagsCollection();
        if (attributes != null)
        {
            foreach (var attr in attributes)
            {
                tagsCollection.Add(attr.Key, attr.Value);
            }
        }

        Activity.Current.AddEvent(new ActivityEvent(eventName, tags: tagsCollection));
    }

    /// <summary>
    /// Records a job phase change in the current Activity
    /// </summary>
    /// <param name="phase">Job phase name (e.g., "Enqueued", "Processing", "Succeeded", "Failed")</param>
    /// <param name="details">Optional details about the phase change</param>
    public static void RecordJobPhase(string phase, string details = null)
    {
        if (Activity.Current == null)
            return;

        // Add the phase as a tag
        Activity.Current.SetTag("hangfire.job.phase", phase);

        // Add phase details if provided
        if (!string.IsNullOrEmpty(details))
        {
            Activity.Current.SetTag("hangfire.job.phase.details", details);
        }

        // Add phase timestamp
        var timestamp = DateTime.UtcNow;
        Activity.Current.SetTag($"hangfire.job.phase.{phase.ToLowerInvariant()}_at", timestamp.ToString("o"));

        // Add an event for the phase change
        var eventAttributes = new Dictionary<string, object>
        {
            ["hangfire.job.phase"] = phase,
            ["timestamp"] = timestamp
        };

        if (!string.IsNullOrEmpty(details))
        {
            eventAttributes["details"] = details;
        }

        TriggerJobEvent($"JobPhase{phase}", eventAttributes);
    }

    /// <summary>
    /// Records a job failure with error details
    /// </summary>
    /// <param name="exception">The exception that caused the failure</param>
    /// <param name="logger">Logger to use for logging the error</param>
    public static void RecordJobFailure(Exception exception, ILogger logger)
    {
        if (Activity.Current == null || exception == null || logger == null)
            return;

        // Set error status on the span
        Activity.Current.SetStatus(ActivityStatusCode.Error);

        // Add error tags
        Activity.Current.SetTag("error", true);
        Activity.Current.SetTag("error.type", exception.GetType().FullName);
        Activity.Current.SetTag("error.message", exception.Message);
        Activity.Current.SetTag("hangfire.job.phase", "Failed");

        // Create error event
        var tagsCollection = new ActivityTagsCollection
        {
            { "exception.type", exception.GetType().FullName },
            { "exception.message", exception.Message },
            { "timestamp", DateTime.UtcNow }
        };

        Activity.Current.AddEvent(new ActivityEvent("JobFailed", tags: tagsCollection));

        // Log the error
        logger.Error(exception, "Hangfire job failed: {ErrorMessage}", exception.Message);
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent == null || propertyFactory == null)
            return;

        // Check for Hangfire availability only once
        if (!_hangfireAvailabilityChecked)
        {
            lock (_lockObject)
            {
                if (!_hangfireAvailabilityChecked)
                {
                    _isHangfireAvailable = AppDomain.CurrentDomain.GetAssemblies()
                        .Any(a => a.GetName().Name?.Equals("Hangfire.Core", StringComparison.OrdinalIgnoreCase) == true);
                    _hangfireAvailabilityChecked = true;
                }
            }
        }

        // Skip enrichment if Hangfire is not available
        if (!_isHangfireAvailable)
            return;

        try
        {
            var hangfireContext = GetHangfireContext();
            if (hangfireContext == null)
            {
                return;
            }

            // Extract job details - with defensive null checks
            string jobId = GetPropertyValue<string>(hangfireContext, "JobId") ?? "unknown";
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("HangfireJobId", jobId));

            // Add job name (method name) if available - with fallbacks
            string jobName = GetPropertyValue<string>(hangfireContext, "Job.Method.Name") ??
                             GetPropertyValue<string>(hangfireContext, "Job.Type.Name") ??
                             "unknown";
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("HangfireJobName", jobName));

            // Add job queue if available
            string queue = GetPropertyValue<string>(hangfireContext, "Queue") ?? "default";
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("HangfireQueue", queue));

            // Initialize queue time with zero
            TimeSpan queueTime = TimeSpan.Zero;

            // Add job creation time if available
            var createdAt = GetPropertyValue<DateTime?>(hangfireContext, "CreatedAt");
            if (createdAt.HasValue)
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("HangfireJobCreatedAt", createdAt));

                // Calculate queue time (time job spent in queue)
                queueTime = DateTime.UtcNow - createdAt.Value;
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("HangfireQueueTimeMs", queueTime.TotalMilliseconds));
            }

            // Set trace context if not already present - with defensive Activity handling
            if (Activity.Current == null)
            {
                var activityName = $"Hangfire.Job.{jobName}";
                ActivitySource activitySource = null;

                try
                {
                    activitySource = new ActivitySource("Hangfire.Jobs");
                    var activity = activitySource.StartActivity(activityName, ActivityKind.Internal);

                    if (activity != null)
                    {
                        SetActivityTagsSafely(activity, jobId, jobName, queue, createdAt, queueTime);
                    }
                }
                catch (Exception ex)
                {
                    // Add error as property instead of failing
                    logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                        "HangfireActivityError", $"Failed to create activity: {ex.Message}"));
                }
            }
            else if (Activity.Current != null)
            {
                try
                {
                    // If there's already an activity, just add Hangfire tags to it
                    SetActivityTagsSafely(Activity.Current, jobId, jobName, queue, createdAt, queueTime);
                }
                catch (Exception ex)
                {
                    // Add error as property instead of failing
                    logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                        "HangfireActivityTagError", $"Failed to set activity tags: {ex.Message}"));
                }
            }
        }
        catch (Exception ex)
        {
            // Enhanced error handling - with more detailed diagnostics
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("HangfireEnricherError", ex.Message));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("HangfireEnricherErrorType", ex.GetType().Name));

            if (ex is ReflectionTypeLoadException rtle && rtle.LoaderExceptions?.Length > 0)
            {
                logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
                    "HangfireEnricherLoaderError",
                    string.Join("; ", rtle.LoaderExceptions.Select(e => e.Message))));
            }
        }
    }

    private void SetActivityTagsSafely(Activity activity, string jobId, string jobName, string queue,
        DateTime? createdAt, TimeSpan queueTime)
    {
        // Add all tags in a try-catch to handle any individual tag failure
        TrySetTag(activity, "hangfire.job.id", jobId);
        TrySetTag(activity, "hangfire.job.name", jobName);
        TrySetTag(activity, "hangfire.queue", queue);

        if (createdAt.HasValue)
        {
            TrySetTag(activity, "hangfire.job.created_at", createdAt.Value.ToString("o"));
            TrySetTag(activity, "hangfire.queue_time_ms", queueTime.TotalMilliseconds);
        }
    }

    private void TrySetTag(Activity activity, string key, object value)
    {
        try
        {
            activity.SetTag(key, value);
        }
        catch
        {
            // Individual tag failures should not break the entire operation
        }
    }

    private static object GetHangfireContext()
    {
        try
        {
            // Skip if Hangfire assembly isn't loaded
            if (!AppDomain.CurrentDomain.GetAssemblies()
                    .Any(a => a.GetName().Name?.Equals("Hangfire.Core", StringComparison.OrdinalIgnoreCase) == true))
            {
                return null;
            }

            // First attempt: try to find JobActivator.Current
            var currentJobActivator = GetStaticField(HangfireAssembly, "Hangfire.JobActivator", "Current");
            if (currentJobActivator != null)
            {
                var context = GetThreadStaticField(HangfireAssembly, "Hangfire.Storage.StateData", "Current");
                if (context != null)
                {
                    return context;
                }
            }

            // Second attempt: try to find BackgroundJobContext.Current directly
            var context2 = GetThreadStaticField(HangfireAssembly, "Hangfire.BackgroundJobContext", "Current");
            if (context2 != null)
            {
                return context2;
            }

            // No Hangfire context found
            return null;
        }
        catch
        {
            // Fail silently if there's any issue
            return null;
        }
    }

    private static object GetStaticField(AssemblyName assemblyName, string typeName, string fieldName)
    {
        try
        {
            // Try to load the assembly
            Assembly assembly = null;
            try
            {
                assembly = Assembly.Load(assemblyName);
            }
            catch
            {
                // Try to find by name if the specific version load fails
                assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name?.Equals(
                        assemblyName.Name, StringComparison.OrdinalIgnoreCase) == true);
            }

            if (assembly == null)
                return null;

            var type = assembly.GetType(typeName);
            var field = type?.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            return field?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    private static object GetThreadStaticField(AssemblyName assemblyName, string typeName, string fieldName)
    {
        try
        {
            // Try to load the assembly
            Assembly assembly = null;
            try
            {
                assembly = Assembly.Load(assemblyName);
            }
            catch
            {
                // Try to find by name if the specific version load fails
                assembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name?.Equals(
                        assemblyName.Name, StringComparison.OrdinalIgnoreCase) == true);
            }

            if (assembly == null)
                return null;

            var type = assembly.GetType(typeName);
            var field = type?.GetField(fieldName, BindingFlags.Public | BindingFlags.Static);
            if (field == null) return null;

            // If ThreadStatic attribute is present or if we're accessing a well-known thread static field
            var threadStaticAttr = field.GetCustomAttribute<ThreadStaticAttribute>();
            if (threadStaticAttr != null || fieldName.Equals("Current", StringComparison.OrdinalIgnoreCase))
            {
                return field.GetValue(null);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static T GetPropertyValue<T>(object obj, string propertyPath)
    {
        if (obj == null) return default;

        try
        {
            var currentObj = obj;
            var properties = propertyPath.Split('.');

            // Defense against empty property paths
            if (properties.Length == 0 || properties.Any(string.IsNullOrWhiteSpace))
                return default;

            foreach (var property in properties)
            {
                // Null check before property access
                if (currentObj == null)
                    return default;

                var prop = currentObj.GetType().GetProperty(property);
                if (prop == null) return default;

                currentObj = prop.GetValue(currentObj);
            }

            // Try direct cast first
            if (currentObj is T result)
            {
                return result;
            }

            // Try conversion if possible
            try
            {
                if (currentObj != null && typeof(T).IsValueType)
                {
                    return (T)Convert.ChangeType(currentObj, typeof(T));
                }
                return default;
            }
            catch
            {
                return default;
            }
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// Disposable scope for a specific Hangfire job
    /// </summary>
    private class HangfireJobScope : IDisposable
    {
        private readonly Activity? _activity;
        private bool _disposed;

        public HangfireJobScope(string jobId, string jobName, string queue, DateTime createdAt)
        {
            // Create a new activity for this job
            var source = new ActivitySource("Hangfire.Jobs");
            _activity = source.StartActivity($"Hangfire.Job.{jobName}", ActivityKind.Internal);

            if (_activity != null)
            {
                // Set standard Hangfire job attributes
                _activity.SetTag("hangfire.job.id", jobId);
                _activity.SetTag("hangfire.job.name", jobName);
                _activity.SetTag("hangfire.queue", queue);
                _activity.SetTag("hangfire.job.created_at", createdAt.ToString("o"));

                // Calculate queue time
                var queueTime = DateTime.UtcNow - createdAt;
                _activity.SetTag("hangfire.queue_time_ms", queueTime.TotalMilliseconds);

                // Record the initial phase
                _activity.SetTag("hangfire.job.phase", "Processing");
                _activity.AddEvent(new ActivityEvent("JobProcessingStarted"));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                if (_activity != null)
                {
                    // Record job completion if not already in error state
                    if (_activity.Status != ActivityStatusCode.Error)
                    {
                        _activity.SetTag("hangfire.job.phase", "Succeeded");
                        _activity.SetStatus(ActivityStatusCode.Ok);
                        _activity.AddEvent(new ActivityEvent("JobProcessingCompleted"));
                    }

                    // Set final processing time
                    _activity.SetTag("hangfire.processing_time_ms", _activity.Duration.TotalMilliseconds);

                    // Dispose the activity
                    _activity.Dispose();
                }
            }
            catch (Exception)
            {
                // Suppress exceptions in Dispose to avoid crashing the application
            }

            _disposed = true;
        }
    }

    /// <summary>
    /// Disposable scope for automatic Hangfire context
    /// </summary>
    private class HangfireScope : IDisposable
    {
        private readonly Activity? _createdActivity;
        private bool _disposed;
        private readonly object? _hangfireContext;

        public HangfireScope()
        {
            // Capture the current context (will be cleared on dispose)
            _hangfireContext = GetHangfireContext();

            // Store the current activity to avoid disposing activities we didn't create
            _createdActivity = Activity.Current;
        }

        public void Dispose()
        {
            if (_disposed) return;

            // No explicit cleanup needed as we're just reading the Hangfire context
            // We don't want to dispose the activity if it's different from what we started with
            if (_createdActivity != null && _createdActivity == Activity.Current)
            {
                _createdActivity.Dispose();
            }

            _disposed = true;
        }
    }
}
