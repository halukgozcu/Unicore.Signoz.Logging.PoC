using Microsoft.AspNetCore.Http;
using Serilog.Core;
using Serilog.Events;

namespace Unicore.Common.OpenTelemetry.Services;

/// <summary>
/// Enriches Serilog log events with properties from the current HttpContext
/// </summary>
public class HttpContextEnricher : ILogEventEnricher
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextEnricher(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return;
        }

        // Add client IP address
        var clientIp = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ClientIp", clientIp));

        // Add request path
        var requestPath = httpContext.Request.Path.ToString();
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RequestPath", requestPath));

        // Add request method
        var requestMethod = httpContext.Request.Method;
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RequestMethod", requestMethod));

        // Add user agent
        var userAgent = httpContext.Request.Headers["User-Agent"].ToString();
        if (!string.IsNullOrEmpty(userAgent))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("UserAgent", userAgent));
        }

        // Add request ID if available
        if (httpContext.Request.Headers.TryGetValue("X-Request-ID", out var requestId) ||
            httpContext.TraceIdentifier != null)
        {
            var id = requestId.ToString() != string.Empty ? requestId.ToString() : httpContext.TraceIdentifier;
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RequestId", id));
        }

        // Add correlation ID if available
        if (httpContext.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlationId.ToString()));
        }
    }
}
