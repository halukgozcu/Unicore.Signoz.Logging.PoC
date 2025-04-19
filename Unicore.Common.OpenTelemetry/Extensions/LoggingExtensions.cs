using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Unicore.Common.OpenTelemetry.Contexts;

namespace Unicore.Common.OpenTelemetry.Extensions;

/// <summary>
/// Extension methods for enhanced contextual logging
/// </summary>
public static class LoggingExtensions
{
    /// <summary>
    /// Logs the beginning of an operation with context
    /// </summary>
    public static IDisposable LogOperationStart<T>(this ILogger<T> logger, string operationName, LogLevel logLevel = LogLevel.Information,
        Dictionary<string, object> properties = null)
    {
        // Set operation ID in context
        string operationId = LoggingContext.SetOperationId(operationName);

        // Set additional properties if provided
        if (properties != null)
        {
            LoggingContext.SetProperties(properties);
        }

        // Start timing the operation
        var timing = LoggingContext.BeginTiming(operationName);

        // Create parent span if none exists
        var activity = Activity.Current;
        ActivitySource source = null;

        if (activity == null)
        {
            source = new ActivitySource(typeof(T).Name);
            activity = source.StartActivity(operationName, ActivityKind.Internal);

            if (activity != null && properties != null)
            {
                foreach (var prop in properties)
                {
                    activity.SetTag(prop.Key, prop.Value);
                }
            }
        }

        // Log operation start
        switch (logLevel)
        {
            case LogLevel.Debug:
                logger.LogDebug("Starting operation {OperationName} [ID: {OperationId}]", operationName, operationId);
                break;
            case LogLevel.Information:
                logger.LogInformation("Starting operation {OperationName} [ID: {OperationId}]", operationName, operationId);
                break;
            case LogLevel.Trace:
                logger.LogTrace("Starting operation {OperationName} [ID: {OperationId}]", operationName, operationId);
                break;
                // Add other levels as needed
        }

        // Return a disposable that logs completion when disposed
        return new OperationLogger<T>(logger, operationName, operationId, timing, activity, source);
    }

    /// <summary>
    /// Logs a business operation with context and result
    /// </summary>
    public static void LogBusinessOperation<T>(this ILogger<T> logger, string operation, string result, object contextData = null)
    {
        using var scope = LoggingContext.PushToLogContext();

        if (contextData != null)
        {
            // Extract properties from the context data
            foreach (var prop in contextData.GetType().GetProperties())
            {
                var value = prop.GetValue(contextData);
                if (value != null)
                {
                    LoggingContext.SetProperty(prop.Name, value);
                }
            }
        }

        logger.LogInformation("[Business Operation: {Operation}] Result: {Result}", operation, result);
    }

    /// <summary>
    /// Logs a security event with appropriate context
    /// </summary>
    public static void LogSecurityEvent<T>(this ILogger<T> logger, string securityAction, string userId,
        string resource, bool success, string details = null)
    {
        using var scope = LoggingContext.PushToLogContext();

        LoggingContext.SetProperty("SecurityEvent", true);
        LoggingContext.SetProperty("SecurityAction", securityAction);
        LoggingContext.SetProperty("UserId", userId);
        LoggingContext.SetProperty("Resource", resource);
        LoggingContext.SetProperty("Success", success);

        if (details != null)
        {
            LoggingContext.SetProperty("SecurityDetails", details);
        }

        if (success)
        {
            logger.LogInformation(
                "[Security] User {UserId} performed {SecurityAction} on {Resource} successfully",
                userId, securityAction, resource);
        }
        else
        {
            logger.LogWarning(
                "[Security] User {UserId} attempted {SecurityAction} on {Resource} but failed: {SecurityDetails}",
                userId, securityAction, resource, details ?? "No details provided");
        }
    }

    /// <summary>
    /// Logs data related operations (query, insert, update, delete) with execution context
    /// </summary>
    public static void LogDataOperation<T>(this ILogger<T> logger, string operation, string entity, int affectedRecords,
        long executionTimeMs, string details = null)
    {
        using var scope = LoggingContext.PushToLogContext();

        LoggingContext.SetProperty("DataOperation", operation);
        LoggingContext.SetProperty("Entity", entity);
        LoggingContext.SetProperty("AffectedRecords", affectedRecords);
        LoggingContext.SetProperty("ExecutionTimeMs", executionTimeMs);

        if (details != null)
        {
            LoggingContext.SetProperty("OperationDetails", details);
        }

        logger.LogInformation(
            "[Data] {DataOperation} on {Entity} affected {AffectedRecords} record(s) in {ExecutionTimeMs}ms",
            operation, entity, affectedRecords, executionTimeMs);
    }

    /// <summary>
    /// Measures and logs the execution time of an action
    /// </summary>
    public static T TimeAndLog<T, TLogger>(this ILogger<TLogger> logger, Func<T> action, string operationName)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            sw.Stop();
            logger.LogInformation("{OperationName} completed in {ElapsedMilliseconds}ms",
                operationName, sw.ElapsedMilliseconds);

            if (Activity.Current != null)
            {
                Activity.Current.SetTag($"{operationName.Replace(" ", "_")}_duration_ms", sw.ElapsedMilliseconds);
            }
        }
    }

    /// <summary>
    /// Measures and logs the execution time of an async action
    /// </summary>
    public static async Task<T> TimeAndLogAsync<T, TLogger>(this ILogger<TLogger> logger, Func<Task<T>> action, string operationName)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await action();
        }
        finally
        {
            sw.Stop();
            logger.LogInformation("{OperationName} completed in {ElapsedMilliseconds}ms",
                operationName, sw.ElapsedMilliseconds);

            if (Activity.Current != null)
            {
                Activity.Current.SetTag($"{operationName.Replace(" ", "_")}_duration_ms", sw.ElapsedMilliseconds);
            }
        }
    }

    private class OperationLogger<T> : IDisposable
    {
        private readonly ILogger<T> _logger;
        private readonly string _operationName;
        private readonly string _operationId;
        private readonly IDisposable _timing;
        private readonly Activity _activity;
        private readonly ActivitySource _source;

        public OperationLogger(ILogger<T> logger, string operationName, string operationId,
            IDisposable timing, Activity activity, ActivitySource source)
        {
            _logger = logger;
            _operationName = operationName;
            _operationId = operationId;
            _timing = timing;
            _activity = activity;
            _source = source;
        }

        public void Dispose()
        {
            // Get elapsed time from context
            if (LoggingContext.TryGetProperty<long>($"{_operationName}DurationMs", out var durationMs))
            {
                _logger.LogInformation("Completed operation {OperationName} [ID: {OperationId}] in {DurationMs}ms",
                    _operationName, _operationId, durationMs);
            }
            else
            {
                _logger.LogInformation("Completed operation {OperationName} [ID: {OperationId}]",
                    _operationName, _operationId);
            }

            // Dispose timing
            _timing?.Dispose();

            // Dispose activity if we created it
            if (_source != null && _activity != null)
            {
                _activity.Dispose();
                _source.Dispose();
            }
        }
    }
}
