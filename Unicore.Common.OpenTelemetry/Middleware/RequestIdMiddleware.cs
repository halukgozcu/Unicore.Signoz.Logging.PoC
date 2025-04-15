using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Unicore.Common.OpenTelemetry.Middleware;

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
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Ensure there's a request ID
        if (!context.Request.Headers.TryGetValue(RequestIdHeaderName, out var requestId) || string.IsNullOrEmpty(requestId))
        {
            requestId = context.TraceIdentifier;
            context.Request.Headers.Append(RequestIdHeaderName, requestId);
        }

        // Make sure we return the request ID in the response
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(RequestIdHeaderName))
            {
                context.Response.Headers.Append(RequestIdHeaderName, requestId);
            }

            return Task.CompletedTask;
        });

        // Generate correlation ID if not present
        if (!context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var correlationId) || string.IsNullOrEmpty(correlationId))
        {
            correlationId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
            context.Request.Headers.Append(CorrelationIdHeaderName, correlationId);
        }

        // Add correlation ID to the response too
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(CorrelationIdHeaderName))
            {
                context.Response.Headers.Append(CorrelationIdHeaderName, correlationId);
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
