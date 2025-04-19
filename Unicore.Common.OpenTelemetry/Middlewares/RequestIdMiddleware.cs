using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Unicore.Common.OpenTelemetry.Middlewares;

/// <summary>
/// Middleware to ensure all requests have a unique request ID
/// </summary>
public class RequestIdMiddleware
{
    private readonly RequestDelegate _next;
    private const string RequestIdHeaderName = "X-Request-ID";
    private const string CorrelationIdHeaderName = "X-Correlation-ID";

    public RequestIdMiddleware(RequestDelegate next)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        try
        {
            // Ensure there's a request ID
            string requestId;
            if (!TryGetRequestId(context, out requestId))
            {
                // Generate a secure request ID if none exists
                requestId = GenerateSecureRequestId();
                SafeAddHeader(context.Request.Headers, RequestIdHeaderName, requestId);
            }

            // Make sure we return the request ID in the response
            context.Response.OnStarting(() =>
            {
                try
                {
                    if (!context.Response.Headers.ContainsKey(RequestIdHeaderName))
                    {
                        SafeAddHeader(context.Response.Headers, RequestIdHeaderName, requestId);
                    }
                }
                catch (Exception)
                {
                    // Suppress header errors - this is non-critical functionality
                }
                return Task.CompletedTask;
            });

            // Generate correlation ID if not present
            string correlationId;
            if (!TryGetCorrelationId(context, out correlationId))
            {
                // Create correlation ID with strong ID generation
                correlationId = GetSafeCorrelationId();
                SafeAddHeader(context.Request.Headers, CorrelationIdHeaderName, correlationId);
            }

            // Add correlation ID to the response too
            context.Response.OnStarting(() =>
            {
                try
                {
                    if (!context.Response.Headers.ContainsKey(CorrelationIdHeaderName))
                    {
                        SafeAddHeader(context.Response.Headers, CorrelationIdHeaderName, correlationId);
                    }
                }
                catch (Exception)
                {
                    // Suppress header errors - this is non-critical functionality
                }
                return Task.CompletedTask;
            });

            await _next(context);
        }
        catch (Exception)
        {
            // Don't let header handling disrupt the request pipeline
            // Just continue with the request even if ID handling fails
            await _next(context);
        }
    }

    private bool TryGetRequestId(HttpContext context, out string requestId)
    {
        requestId = string.Empty;
        try
        {
            if (context.Request.Headers.TryGetValue(RequestIdHeaderName, out var headerValue) &&
                !string.IsNullOrEmpty(headerValue))
            {
                // Validate the request ID format
                if (IsValidId(headerValue))
                {
                    requestId = headerValue;
                    return true;
                }
            }

            // Use trace identifier as fallback
            if (!string.IsNullOrEmpty(context.TraceIdentifier))
            {
                requestId = context.TraceIdentifier;
                return true;
            }
        }
        catch
        {
            // If header extraction fails, we'll generate a new ID
        }
        return false;
    }

    private bool TryGetCorrelationId(HttpContext context, out string correlationId)
    {
        correlationId = string.Empty;
        try
        {
            if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var headerValue) &&
                !string.IsNullOrEmpty(headerValue))
            {
                // Validate the correlation ID format
                if (IsValidId(headerValue))
                {
                    correlationId = headerValue;
                    return true;
                }
            }
        }
        catch
        {
            // If header extraction fails, we'll generate a new ID
        }
        return false;
    }

    private string GetSafeCorrelationId()
    {
        // First try to use Activity.Current.Id
        if (Activity.Current?.Id != null)
        {
            return Activity.Current.Id;
        }

        // Otherwise, generate a new ID
        return GenerateSecureRequestId();
    }

    private string GenerateSecureRequestId()
    {
        // Generate a request ID with better uniqueness than just Guid
        return $"req_{Guid.NewGuid():N}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
    }

    private bool IsValidId(string id)
    {
        // Perform basic validation of ID format
        return !string.IsNullOrWhiteSpace(id) &&
               id.Length < 128 && // Prevent unreasonably long IDs
               !id.Contains('\r') && !id.Contains('\n'); // Basic security check
    }

    private void SafeAddHeader(IHeaderDictionary headers, string name, string value)
    {
        try
        {
            if (headers.ContainsKey(name))
            {
                headers[name] = value;
            }
            else
            {
                headers.Append(name, value);
            }
        }
        catch (Exception)
        {
            // Ignore header manipulation errors - this is non-critical
        }
    }
}
