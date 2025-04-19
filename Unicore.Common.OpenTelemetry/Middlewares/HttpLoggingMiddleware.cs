using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Unicore.Common.OpenTelemetry.Middlewares;

/// <summary>
/// Middleware for logging inbound HTTP requests and responses
/// </summary>
public class HttpLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<HttpLoggingMiddleware> _logger;
    private readonly HttpLoggingOptions _options;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public HttpLoggingMiddleware(
        RequestDelegate next,
        ILogger<HttpLoggingMiddleware> logger,
        IOptions<HttpLoggingOptions> options)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new HttpLoggingOptions();
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        // Check if we should skip logging for this request
        if (ShouldSkip(context.Request))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var requestId = context.TraceIdentifier;
        var requestBody = string.Empty;

        // Enable request buffering so we can read it multiple times
        context.Request.EnableBuffering();

        // Capture request details
        if (_options.LogRequestBody && context.Request.ContentLength > 0 &&
            context.Request.ContentLength <= _options.MaxBodySize)
        {
            try
            {
                // Read the request body
                using var reader = new StreamReader(
                    context.Request.Body,
                    encoding: Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: false,
                    leaveOpen: true);

                requestBody = await reader.ReadToEndAsync();

                // Reset the position so the request can be processed normally
                context.Request.Body.Position = 0;

                // Redact sensitive information if configured
                requestBody = RedactSensitiveData(requestBody);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unable to read request body for logging. Request ID: {RequestId}", requestId);
            }
        }

        // Log the incoming request
        _logger.LogInformation(
            "HTTP Request: {Method} {Path} | TraceId: {TraceId} | Headers: {Headers} {RequestBody}",
            context.Request.Method,
            GetDisplayUrl(context.Request),
            Activity.Current?.TraceId.ToString() ?? "None",
            FormatHeaders(context.Request.Headers, _options.ExcludedRequestHeaders),
            !string.IsNullOrEmpty(requestBody) ? $"| Body: {requestBody}" : string.Empty);

        // Create a buffer for the response
        var originalBodyStream = context.Response.Body;
        using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        try
        {
            // Call the next middleware in the pipeline
            await _next(context);
        }
        catch (Exception ex)
        {
            // If an exception occurs in subsequent middleware, log it and rethrow
            _logger.LogError(ex,
                "Exception caught in HttpLoggingMiddleware. Request: {Method} {Path}",
                context.Request.Method,
                GetDisplayUrl(context.Request));
            throw;
        }
        finally
        {
            // Even if an exception occurred, attempt to log the response if one was generated
            stopwatch.Stop();
            var responseBody = string.Empty;

            // Capture response details
            if (_options.LogResponseBody && responseBuffer.Length > 0 && responseBuffer.Length <= _options.MaxBodySize)
            {
                try
                {
                    // Reset buffer position and read response
                    responseBuffer.Position = 0;
                    using var reader = new StreamReader(responseBuffer, Encoding.UTF8);
                    responseBody = await reader.ReadToEndAsync();

                    // Redact sensitive data
                    responseBody = RedactSensitiveData(responseBody);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unable to read response body for logging. Request ID: {RequestId}", requestId);
                }
            }

            // Log the response
            _logger.LogInformation(
                "HTTP Response: {StatusCode} | TraceId: {TraceId} | Duration: {ElapsedMilliseconds}ms | Headers: {Headers} {ResponseBody}",
                context.Response.StatusCode,
                Activity.Current?.TraceId.ToString() ?? "None",
                stopwatch.ElapsedMilliseconds,
                FormatHeaders(context.Response.Headers, _options.ExcludedResponseHeaders),
                !string.IsNullOrEmpty(responseBody) ? $"| Body: {responseBody}" : string.Empty);

            // Copy the response back to the original stream if needed
            if (responseBuffer.Length > 0)
            {
                responseBuffer.Position = 0;
                await responseBuffer.CopyToAsync(originalBodyStream);
            }

            // Restore the original stream
            context.Response.Body = originalBodyStream;
        }
    }

    private bool ShouldSkip(HttpRequest request)
    {
        var path = request.Path.ToString();

        // Skip static files by extension
        if (_options.SkipStaticFiles &&
            (path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".css", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Skip health check endpoints
        if (_options.SkipHealthChecks &&
            (path.Contains("/health", StringComparison.OrdinalIgnoreCase) ||
             path.Contains("/liveness", StringComparison.OrdinalIgnoreCase) ||
             path.Contains("/readiness", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Skip endpoints matching excluded paths patterns
        if (_options.ExcludedPaths != null && _options.ExcludedPaths.Count > 0)
        {
            foreach (var excludePattern in _options.ExcludedPaths)
            {
                if (path.Contains(excludePattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private string GetDisplayUrl(HttpRequest request)
    {
        var displayUrl = $"{request.Path}{request.QueryString}";

        // For security, we might not want to log the full query string in production
        if (_options.RedactQueryStringParameters && !string.IsNullOrEmpty(request.QueryString.Value))
        {
            displayUrl = $"{request.Path}?[redacted]";
        }

        return displayUrl;
    }

    private string FormatHeaders(IHeaderDictionary headers, IList<string> excludedHeaders)
    {
        if (headers == null || headers.Count == 0 || !_options.LogHeaders)
        {
            return string.Empty;
        }

        var items = new Dictionary<string, string>();

        foreach (var header in headers)
        {
            // Skip excluded headers (case-insensitive)
            if (excludedHeaders != null &&
                excludedHeaders.Any(h => h.Equals(header.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Skip Authorization header or log it redacted based on option
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                if (_options.LogAuthorizationHeader)
                {
                    items.Add(header.Key, "[redacted]");
                }
                continue;
            }

            var headerValue = header.Value.ToString();

            // Redact cookie values if configured
            if (header.Key.Equals("Cookie", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
            {
                if (_options.RedactCookieValues)
                {
                    items.Add(header.Key, "[redacted]");
                    continue;
                }
            }

            items.Add(header.Key, headerValue);
        }

        return JsonSerializer.Serialize(items, _jsonOptions);
    }

    private string RedactSensitiveData(string content)
    {
        if (string.IsNullOrEmpty(content) || _options.SensitiveDataPatterns == null)
        {
            return content;
        }

        // Apply each regex pattern to redact matching data
        var redactedContent = content;
        foreach (var pattern in _options.SensitiveDataPatterns)
        {
            try
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                redactedContent = regex.Replace(redactedContent, "[redacted]");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error applying redaction pattern: {Pattern}", pattern);
            }
        }

        return redactedContent;
    }
}

/// <summary>
/// Configuration options for HTTP logging middleware
/// </summary>
public class HttpLoggingOptions
{
    /// <summary>
    /// Whether to log request bodies
    /// </summary>
    public bool LogRequestBody { get; set; } = true;

    /// <summary>
    /// Whether to log response bodies
    /// </summary>
    public bool LogResponseBody { get; set; } = true;

    /// <summary>
    /// Whether to log request and response headers
    /// </summary>
    public bool LogHeaders { get; set; } = true;

    /// <summary>
    /// Whether to log the Authorization header (redacted)
    /// </summary>
    public bool LogAuthorizationHeader { get; set; } = false;

    /// <summary>
    /// Whether to redact cookie values in logs
    /// </summary>
    public bool RedactCookieValues { get; set; } = true;

    /// <summary>
    /// Whether to redact query string parameters
    /// </summary>
    public bool RedactQueryStringParameters { get; set; } = false;

    /// <summary>
    /// Maximum size of request/response body to log in bytes (default 100KB)
    /// </summary>
    public long MaxBodySize { get; set; } = 100 * 1024;

    /// <summary>
    /// Whether to skip logging of static files
    /// </summary>
    public bool SkipStaticFiles { get; set; } = true;

    /// <summary>
    /// Whether to skip logging of health check endpoints
    /// </summary>
    public bool SkipHealthChecks { get; set; } = true;

    /// <summary>
    /// Paths to exclude from logging
    /// </summary>
    public List<string> ExcludedPaths { get; set; } = new();

    /// <summary>
    /// Request headers to exclude from logging
    /// </summary>
    public List<string> ExcludedRequestHeaders { get; set; } = new()
    {
        "Cookie",
        "X-API-Key"
    };

    /// <summary>
    /// Response headers to exclude from logging
    /// </summary>
    public List<string> ExcludedResponseHeaders { get; set; } = new()
    {
        "Set-Cookie"
    };

    /// <summary>
    /// Regex patterns for sensitive data that should be redacted
    /// </summary>
    public List<string> SensitiveDataPatterns { get; set; } = new()
    {
        @"""password""\s*:\s*""[^""]*""",
        @"""ssn""\s*:\s*""[^""]*""",
        @"""creditcard""\s*:\s*""[^""]*""",
        @"""cardnumber""\s*:\s*""[^""]*""",
        @"""securitycode""\s*:\s*""[^""]*""",
        @"""token""\s*:\s*""[^""]*""",
        @"""apikey""\s*:\s*""[^""]*""",
        @"""api[-_]?key""\s*:\s*""[^""]*""",
        @"""secret""\s*:\s*""[^""]*""",
        @"""access[-_]?token""\s*:\s*""[^""]*""",
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}\b" // Email addresses
    };
}

/// <summary>
/// Delegating handler to log outbound HTTP requests
/// </summary>
public class HttpClientLoggingDelegatingHandler : DelegatingHandler
{
    private readonly ILogger<HttpClientLoggingDelegatingHandler> _logger;
    private readonly HttpLoggingOptions _options;

    public HttpClientLoggingDelegatingHandler(
        ILogger<HttpClientLoggingDelegatingHandler> logger,
        IOptions<HttpLoggingOptions>? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new HttpLoggingOptions();
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));

        var requestId = Activity.Current?.Id ?? Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();
        var requestBody = string.Empty;

        // Log the outgoing request
        if (_options.LogRequestBody && request.Content != null)
        {
            try
            {
                requestBody = await request.Content.ReadAsStringAsync(cancellationToken);

                // If body exceeds limits, truncate it
                if (requestBody.Length > _options.MaxBodySize)
                {
                    requestBody = $"{requestBody[..(int)_options.MaxBodySize]}... [truncated, {requestBody.Length} bytes total]";
                }

                // Redact sensitive data
                requestBody = RedactSensitiveData(requestBody);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reading outgoing request body for logging. Request ID: {RequestId}", requestId);
            }
        }

        // Prepare request headers for logging
        var requestHeaders = FormatHttpRequestHeaders(request.Headers);

        _logger.LogInformation(
            "HTTP Outgoing Request: {Method} {Uri} | TraceId: {TraceId} | Headers: {Headers} {RequestBody}",
            request.Method,
            request.RequestUri,
            Activity.Current?.TraceId.ToString() ?? "None",
            requestHeaders,
            !string.IsNullOrEmpty(requestBody) ? $"| Body: {requestBody}" : string.Empty);

        try
        {
            // Send the request
            var response = await base.SendAsync(request, cancellationToken);

            stopwatch.Stop();
            var responseBody = string.Empty;

            // Log the response received
            if (_options.LogResponseBody && response.Content != null)
            {
                try
                {
                    responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                    // If body exceeds limits, truncate it
                    if (responseBody.Length > _options.MaxBodySize)
                    {
                        responseBody = $"{responseBody[..(int)_options.MaxBodySize]}... [truncated, {responseBody.Length} bytes total]";
                    }

                    // Redact sensitive data
                    responseBody = RedactSensitiveData(responseBody);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error reading outgoing request response body for logging. Request ID: {RequestId}", requestId);
                }
            }

            // Prepare response headers for logging
            var responseHeaders = FormatHttpResponseHeaders(response.Headers);

            _logger.LogInformation(
                "HTTP Outgoing Response: {StatusCode} | TraceId: {TraceId} | Uri: {Uri} | Duration: {ElapsedMilliseconds}ms | Headers: {Headers} {ResponseBody}",
                (int)response.StatusCode,
                Activity.Current?.TraceId.ToString() ?? "None",
                request.RequestUri,
                stopwatch.ElapsedMilliseconds,
                responseHeaders,
                !string.IsNullOrEmpty(responseBody) ? $"| Body: {responseBody}" : string.Empty);

            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex,
                "HTTP Outgoing Request Failed: {Method} {Uri} | TraceId: {TraceId} | Duration: {ElapsedMilliseconds}ms | Error: {ErrorMessage}",
                request.Method,
                request.RequestUri,
                Activity.Current?.TraceId.ToString() ?? "None",
                stopwatch.ElapsedMilliseconds,
                ex.Message);

            throw;
        }
    }

    private string FormatHttpRequestHeaders(HttpRequestHeaders headers)
    {
        if (headers == null || !headers.Any() || !_options.LogHeaders)
        {
            return string.Empty;
        }

        var items = new Dictionary<string, string>();

        foreach (var header in headers)
        {
            // Skip excluded headers
            if (_options.ExcludedRequestHeaders != null &&
                _options.ExcludedRequestHeaders.Any(h => h.Equals(header.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Handle Authorization header
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                if (_options.LogAuthorizationHeader)
                {
                    items.Add(header.Key, "[redacted]");
                }
                continue;
            }

            items.Add(header.Key, string.Join(", ", header.Value));
        }

        return JsonSerializer.Serialize(items);
    }

    private string FormatHttpResponseHeaders(HttpResponseHeaders headers)
    {
        if (headers == null || !headers.Any() || !_options.LogHeaders)
        {
            return string.Empty;
        }

        var items = new Dictionary<string, string>();

        foreach (var header in headers)
        {
            // Skip excluded headers
            if (_options.ExcludedResponseHeaders != null &&
                _options.ExcludedResponseHeaders.Any(h => h.Equals(header.Key, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            // Handle Authorization header
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                if (_options.LogAuthorizationHeader)
                {
                    items.Add(header.Key, "[redacted]");
                }
                continue;
            }

            items.Add(header.Key, string.Join(", ", header.Value));
        }

        return JsonSerializer.Serialize(items);
    }

    private string RedactSensitiveData(string content)
    {
        if (string.IsNullOrEmpty(content) || _options.SensitiveDataPatterns == null)
        {
            return content;
        }

        // Apply each regex pattern to redact matching data
        var redactedContent = content;
        foreach (var pattern in _options.SensitiveDataPatterns)
        {
            try
            {
                var regex = new Regex(pattern, RegexOptions.IgnoreCase);
                redactedContent = regex.Replace(redactedContent, "[redacted]");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error applying redaction pattern: {Pattern}", pattern);
            }
        }

        return redactedContent;
    }
}

/// <summary>
/// Extension methods for setting up HTTP logging 
/// </summary>
public static class HttpLoggingExtensions
{
    /// <summary>
    /// Adds HTTP request-response logging middleware
    /// </summary>
    public static IApplicationBuilder UseHttpLogging(
        this IApplicationBuilder app,
        Action<HttpLoggingOptions>? configureOptions = null)
    {
        if (app == null)
            throw new ArgumentNullException(nameof(app));

        var options = new HttpLoggingOptions();
        configureOptions?.Invoke(options);

        return app.UseMiddleware<HttpLoggingMiddleware>(Options.Create(options));
    }

    /// <summary>
    /// Adds HTTP logging services to the service collection
    /// </summary>
    public static IServiceCollection AddHttpLogging(
        this IServiceCollection services,
        Action<HttpLoggingOptions>? configureOptions = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }

        services.AddTransient<HttpClientLoggingDelegatingHandler>();

        return services;
    }

    /// <summary>
    /// Adds HTTP client with logging to the service collection
    /// </summary>
    public static IHttpClientBuilder AddHttpClientWithLogging<TClient, TImplementation>(
        this IServiceCollection services,
        Action<HttpClient>? configureClient = null)
        where TClient : class
        where TImplementation : class, TClient
    {
        var clientBuilder = services.AddHttpClient<TClient, TImplementation>(configureClient)
            .AddHttpMessageHandler<HttpClientLoggingDelegatingHandler>();

        return clientBuilder;
    }
}