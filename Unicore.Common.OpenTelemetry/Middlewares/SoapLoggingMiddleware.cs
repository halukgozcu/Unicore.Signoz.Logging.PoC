using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Unicore.Common.OpenTelemetry.Helpers;

namespace Unicore.Common.OpenTelemetry.Middlewares;
public class SoapLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SoapLoggingMiddleware> _logger;

    public SoapLoggingMiddleware(RequestDelegate next, ILogger<SoapLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Handle request
        context.Request.EnableBuffering();
        string requestBody = await ReadBodyAsync(context.Request.Body);
        context.Request.Body.Position = 0;

        string maskedRequestXml = XmlMaskingHelper.MaskSensitiveData(requestBody);
        _logger.LogInformation("SOAP Request:\n{Request}", maskedRequestXml);

        // Handle response
        var originalBody = context.Response.Body;
        using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        await _next(context);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        string responseBody = await ReadBodyAsync(context.Response.Body);
        context.Response.Body.Seek(0, SeekOrigin.Begin);

        string maskedResponseXml = XmlMaskingHelper.MaskSensitiveData(responseBody);
        _logger.LogInformation("SOAP Response:\n{Response}", maskedResponseXml);

        await responseBuffer.CopyToAsync(originalBody);
        context.Response.Body = originalBody;
    }

    private static async Task<string> ReadBodyAsync(Stream body)
    {
        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}