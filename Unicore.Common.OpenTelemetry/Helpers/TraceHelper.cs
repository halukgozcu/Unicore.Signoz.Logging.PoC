using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using System.Net;

namespace Unicore.Common.OpenTelemetry.Helpers;

public static class TraceHelper
{
    /// <summary>
    /// Records an exception in the current activity with detailed metadata
    /// </summary>
    public static void RecordException(Exception exception, ILogger logger, string operation, string component = "unknown")
    {
        if (Activity.Current != null)
        {
            // Set error status on the span
            Activity.Current.SetStatus(ActivityStatusCode.Error);

            // Add detailed tags about the exception
            Activity.Current.SetTag("error", true);
            Activity.Current.SetTag("error.type", exception.GetType().FullName);
            Activity.Current.SetTag("error.message", exception.Message);
            Activity.Current.SetTag("error.stack", exception.StackTrace);
            Activity.Current.SetTag("error.component", component);
            Activity.Current.SetTag("error.operation", operation);

            if (exception.InnerException != null)
            {
                Activity.Current.SetTag("error.cause", exception.InnerException.Message);
            }

            // Create exception event with all details
            var tagsCollection = new ActivityTagsCollection
            {
                { "exception.type", exception.GetType().FullName },
                { "exception.message", exception.Message },
                { "exception.stacktrace", exception.StackTrace },
                { "component", component },
                { "operation", operation }
            };

            Activity.Current.AddEvent(new ActivityEvent("exception", default, tagsCollection));
        }

        // Log the exception with context
        logger.LogError(exception, "[{Component}] Error during {Operation}: {ErrorMessage}",
            component, operation, exception.Message);
    }

    /// <summary>
    /// Records a warning condition with detailed metadata
    /// </summary>
    public static void RecordWarning(ILogger logger, string message, string reason, string component, Dictionary<string, object> attributes)
    {
        if (Activity.Current != null)
        {
            // Add warning attributes
            Activity.Current.SetTag("warning", true);
            Activity.Current.SetTag("warning.message", message);
            Activity.Current.SetTag("warning.reason", reason);
            Activity.Current.SetTag("warning.component", component);

            // Add all custom attributes
            foreach (var attribute in attributes)
            {
                Activity.Current.SetTag(attribute.Key, attribute.Value);
            }

            // Create warning event
            var tagsCollection = new ActivityTagsCollection
            {
                { "warning.message", message },
                { "warning.reason", reason },
                { "component", component }
            };

            foreach (var attribute in attributes)
            {
                tagsCollection.Add(attribute.Key, attribute.Value);
            }

            Activity.Current.AddEvent(new ActivityEvent("warning", default, tagsCollection));
        }

        // Log the warning with context
        logger.LogWarning("[{Component}] {Message}. Reason: {Reason}",
            component, message, reason);
    }

    /// <summary>
    /// Records HTTP status code information in the current activity
    /// </summary>
    public static void RecordHttpResponse(HttpStatusCode statusCode, string endpoint, string reason, ILogger logger)
    {
        if (Activity.Current != null)
        {
            // Add status code tags
            Activity.Current.SetTag("http.status_code", (int)statusCode);
            Activity.Current.SetTag("http.endpoint", endpoint);
            Activity.Current.SetTag("http.status_text", statusCode.ToString());

            if ((int)statusCode >= 400)
            {
                Activity.Current.SetTag("error", true);
                Activity.Current.SetTag("error.reason", reason);

                // For 4xx errors
                if ((int)statusCode < 500)
                {
                    Activity.Current.SetStatus(ActivityStatusCode.Error, $"HTTP {statusCode}: {reason}");
                }
                // For 5xx errors
                else
                {
                    Activity.Current.SetStatus(ActivityStatusCode.Error, $"HTTP {statusCode}: {reason}");
                }

                // Create HTTP error event
                var tagsCollection = new ActivityTagsCollection
                {
                    { "http.status_code", (int)statusCode },
                    { "http.endpoint", endpoint },
                    { "http.status_text", statusCode.ToString() },
                    { "error.reason", reason }
                };

                Activity.Current.AddEvent(new ActivityEvent("http.error", default, tagsCollection));
            }
            else
            {
                Activity.Current.SetStatus(ActivityStatusCode.Ok);
            }
        }

        // Log based on status code
        if ((int)statusCode >= 400 && (int)statusCode < 500)
        {
            logger.LogWarning("HTTP {StatusCode} on {Endpoint}: {Reason}",
                (int)statusCode, endpoint, reason);
        }
        else if ((int)statusCode >= 500)
        {
            logger.LogError("HTTP {StatusCode} on {Endpoint}: {Reason}",
                (int)statusCode, endpoint, reason);
        }
        else
        {
            logger.LogInformation("HTTP {StatusCode} on {Endpoint}",
                (int)statusCode, endpoint);
        }
    }

    /// <summary>
    /// Creates a nested span (activity) with the specified name
    /// </summary>
    public static Activity? CreateNestedSpan(string name, string type, IDictionary<string, object?> attributes)
    {
        var activity = new ActivitySource(type).StartActivity(name, ActivityKind.Internal);

        if (activity != null)
        {
            foreach (var attribute in attributes)
            {
                activity.SetTag(attribute.Key, attribute.Value);
            }
        }

        return activity;
    }
}
