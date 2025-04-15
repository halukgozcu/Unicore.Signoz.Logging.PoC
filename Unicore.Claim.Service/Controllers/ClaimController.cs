using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Unicore.Claim.Service.Services;
using Unicore.Common.OpenTelemetry.Configuration;

namespace Unicore.Claim.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClaimController : ControllerBase
{
    private readonly IClaimProcessorService _claimProcessorService;
    private readonly ILogger<ClaimController> _logger;
    private readonly TelemetryConfig _telemetryConfig;

    public ClaimController(
        IClaimProcessorService claimProcessorService,
        ILogger<ClaimController> logger,
        TelemetryConfig telemetryConfig)
    {
        _claimProcessorService = claimProcessorService;
        _logger = logger;
        _telemetryConfig = telemetryConfig;
    }

    [HttpPost("process")]
    public async Task<IActionResult> ProcessClaim([FromBody] ClaimRequest request)
    {
        _logger.LogInformation("Received claim process request for {ClaimId} from client {ClientIp}",
            request.ClaimId, HttpContext.Connection.RemoteIpAddress);

        // Get trace information from current activity
        var activity = Activity.Current;

        // Start timing for metrics
        var stopwatch = Stopwatch.StartNew();

        // Add claim details to activity tags
        activity?.AddTag("claim.id", request.ClaimId);
        activity?.AddTag("claim.policy_number", request.PolicyNumber);
        activity?.AddTag("claim.amount", request.Amount);

        try
        {
            // Increment request counter
            _telemetryConfig.RequestCounter.Add(1, new KeyValuePair<string, object?>("endpoint", "process_claim"));

            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["ClaimId"] = request.ClaimId,
                ["PolicyNumber"] = request.PolicyNumber,
                ["Amount"] = request.Amount
            }))
            {
                var result = await _claimProcessorService.ProcessClaimAsync(request);

                // Record the successful outcome
                activity?.SetTag("claim.status", result.Status);

                // Record request duration
                stopwatch.Stop();
                _telemetryConfig.RequestDuration.Record(stopwatch.ElapsedMilliseconds,
                    new KeyValuePair<string, object?>("endpoint", "process_claim"),
                    new KeyValuePair<string, object?>("success", "true"));

                // Increment success counter
                _telemetryConfig.SuccessfulRequests.Add(1, new KeyValuePair<string, object?>("endpoint", "process_claim"));

                _logger.LogInformation("Successfully processed claim {ClaimId} with status {Status} in {ProcessingTime}ms",
                    request.ClaimId, result.Status, stopwatch.ElapsedMilliseconds);

                return Ok(result);
            }
        }
        catch (Exception ex)
        {
            // Record the failure for tracing
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            // Record request duration for failed requests too
            stopwatch.Stop();
            _telemetryConfig.RequestDuration.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("endpoint", "process_claim"),
                new KeyValuePair<string, object?>("success", "false"));

            // Increment failure counter
            _telemetryConfig.FailedRequests.Add(1, new KeyValuePair<string, object?>("endpoint", "process_claim"));

            _logger.LogError(ex, "Error processing claim {ClaimId} from {ClientIp}. Error: {ErrorMessage}",
                request.ClaimId, HttpContext.Connection.RemoteIpAddress, ex.Message);

            return StatusCode(500, new
            {
                Error = "An error occurred while processing the claim",
                TraceId = activity?.TraceId.ToString() ?? HttpContext.TraceIdentifier
            });
        }
    }

    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers["User-Agent"].ToString();

        _logger.LogInformation("Health check requested from {ClientIp} using {UserAgent}", clientIp, userAgent);

        return Ok(new
        {
            Status = "Healthy",
            Service = _telemetryConfig.DisplayName,
            Timestamp = DateTimeOffset.UtcNow,
            Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0"
        });
    }
}
