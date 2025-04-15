using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Unicore.Claim.Service.Models;
using Unicore.Common.OpenTelemetry.Configuration;
using Unicore.Common.OpenTelemetry.Helpers;

namespace Unicore.Claim.Service.Services;

public class ClaimProcessorService : IClaimProcessorService
{
    private readonly ILogger<ClaimProcessorService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TelemetryConfig _telemetryConfig;

    public ClaimProcessorService(
        ILogger<ClaimProcessorService> logger,
        IHttpContextAccessor httpContextAccessor,
        IHttpClientFactory httpClientFactory,
        TelemetryConfig telemetryConfig)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _httpClientFactory = httpClientFactory;
        _telemetryConfig = telemetryConfig;
    }

    public async Task<ClaimProcessingResult> ProcessClaimAsync(ClaimRequest claimRequest)
    {
        var clientIp = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        // Start main processing span - this will be a child of the HTTP request span
        using var processingActivity = _telemetryConfig.ActivitySource.StartActivity("ProcessClaim");
        processingActivity?.SetTag("claim.id", claimRequest.ClaimId);
        processingActivity?.SetTag("claim.policy_number", claimRequest.PolicyNumber);
        processingActivity?.SetTag("claim.amount", claimRequest.Amount);
        processingActivity?.SetTag("client.ip", clientIp);

        var stopwatch = Stopwatch.StartNew();
        int retryCount = 0;
        var result = new ClaimProcessingResult();

        _telemetryConfig.RequestCounter.Add(1,
            new KeyValuePair<string, object?>("operation", "ProcessClaim"));

        processingActivity?.AddEvent(new ActivityEvent("ClaimProcessingStarted",
            tags: new ActivityTagsCollection {
                { "claim.id", claimRequest.ClaimId },
                { "claim.amount", claimRequest.Amount }
            }));

        _logger.LogInformation(
            "Processing claim {ClaimId} for policy {PolicyNumber} with amount {Amount:C} ",
            claimRequest.ClaimId,
            claimRequest.PolicyNumber,
            claimRequest.Amount);

        try
        {
            // Implement fraud detection as a child span
            using (var fraudActivity = TraceHelper.CreateNestedSpan("FraudDetection", "Security",
                new Dictionary<string, object?>
                {
                    ["claim.id"] = claimRequest.ClaimId,
                    ["claim.amount"] = claimRequest.Amount
                }))
            {
                await Task.Delay(50); // Simulate fraud check

                // Randomly detect high-risk claim (1 in 10 chance)
                if (new Random().Next(10) == 0)
                {
                    // Record warning for suspicious activity
                    TraceHelper.RecordWarning(_logger,
                        "Suspicious claim activity detected",
                        "Amount exceeds normal threshold for this policy type",
                        "FraudDetection",
                        new Dictionary<string, object>
                        {
                            ["claim.id"] = claimRequest.ClaimId,
                            ["claim.amount"] = claimRequest.Amount,
                            ["risk.score"] = 0.85,
                            ["risk.factors"] = "large_amount,unusual_timing"
                        });

                    fraudActivity?.SetTag("fraud.detected", true);
                    fraudActivity?.SetTag("fraud.risk_score", 0.85);
                    fraudActivity?.SetTag("fraud.action", "flag");

                    processingActivity?.SetTag("claim.flagged", true);
                    processingActivity?.SetTag("claim.risk_score", 0.85);

                    result.Status = "FlaggedForReview";
                    result.Notes = "Claim flagged for manual review due to suspicious activity";

                    // Record HTTP response with Warning
                    TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.OK, "/api/claim/process",
                        "Claim accepted but flagged for review", _logger);

                    // Still process but with a warning
                    _logger.LogWarning("Claim {ClaimId} flagged for review with risk score {RiskScore}",
                        claimRequest.ClaimId, 0.85);
                }

                fraudActivity?.AddEvent(new ActivityEvent("FraudCheckCompleted"));
            }

            // Validate policy by making HTTP request to Policy Service
            bool policyIsValid = false;
            decimal coverage = 0;

            using (var policyActivity = TraceHelper.CreateNestedSpan("PolicyValidation", "ExternalService",
                new Dictionary<string, object?>
                {
                    ["policy.number"] = claimRequest.PolicyNumber
                }))
            {
                try
                {
                    // Create HttpClient with automatic distributed tracing
                    var client = _httpClientFactory.CreateClient("PolicyService");

                    // The OpenTelemetry HTTP instrumentation will automatically
                    // create child spans and propagate the trace context
                    var response = await client.GetAsync($"http://localhost:1202/api/policy/validate/{claimRequest.PolicyNumber}");

                    if (response.IsSuccessStatusCode)
                    {
                        var data = await response.Content.ReadFromJsonAsync<PolicyValidationResult>();
                        policyIsValid = data?.IsValid ?? false;
                        coverage = data?.Coverage ?? 0;

                        policyActivity?.SetTag("policy.valid", policyIsValid);
                        policyActivity?.SetTag("policy.coverage", coverage);

                        if (!policyIsValid)
                        {
                            processingActivity?.SetTag("claim.rejected", true);
                            processingActivity?.SetTag("claim.rejection_reason", "Invalid policy");

                            TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.BadRequest,
                                "/api/claim/process", "Invalid policy", _logger);

                            result.Status = "Rejected";
                            result.Notes = "Policy validation failed";
                            return result;
                        }
                    }
                    else
                    {
                        // Handle error response from Policy Service
                        var errorContent = await response.Content.ReadAsStringAsync();

                        TraceHelper.RecordHttpResponse(response.StatusCode,
                            "http://localhost:1202/api/policy/validate",
                            $"Policy service error: {errorContent}", _logger);

                        policyActivity?.SetTag("error", true);
                        policyActivity?.SetTag("error.status_code", (int)response.StatusCode);
                        policyActivity?.SetTag("error.message", errorContent);

                        processingActivity?.SetTag("claim.rejected", true);
                        processingActivity?.SetTag("claim.rejection_reason", "Policy service error");

                        result.Status = "Error";
                        result.Notes = $"Policy validation error: {response.StatusCode}";
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    // Record the exception in the policy validation span
                    TraceHelper.RecordException(ex, _logger, "PolicyServiceRequest", "HttpClient");

                    throw; // Will be caught by outer try/catch
                }
            }

            // Check if coverage is sufficient (with potential for error)
            using (var coverageActivity = TraceHelper.CreateNestedSpan("CoverageVerification", "BusinessLogic",
                new Dictionary<string, object?>
                {
                    ["claim.amount"] = claimRequest.Amount,
                    ["policy.coverage"] = coverage
                }))
            {
                try
                {
                    // Randomly simulate an error (1 in 20 chance)
                    if (new Random().Next(20) == 0)
                    {
                        throw new InvalidOperationException("Coverage calculation engine failed");
                    }

                    if (claimRequest.Amount > coverage)
                    {
                        coverageActivity?.SetTag("coverage.sufficient", false);
                        coverageActivity?.SetTag("coverage.exceeded_by", claimRequest.Amount - coverage);

                        processingActivity?.SetTag("claim.rejected", true);
                        processingActivity?.SetTag("claim.rejection_reason", "Insufficient coverage");

                        TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.BadRequest,
                            "/api/claim/process", "Claim exceeds policy coverage", _logger);

                        _logger.LogWarning("Claim {ClaimId} amount {Amount:C} exceeds policy coverage {Coverage:C}",
                            claimRequest.ClaimId, claimRequest.Amount, coverage);

                        result.Status = "Rejected";
                        result.Notes = $"Claim exceeds policy coverage limit of {coverage:C}";
                        return result;
                    }

                    coverageActivity?.SetTag("coverage.sufficient", true);
                    coverageActivity?.SetTag("coverage.remaining", coverage - claimRequest.Amount);
                }
                catch (Exception ex)
                {
                    // Record the exception in the coverage verification span
                    TraceHelper.RecordException(ex, _logger, "CoverageVerification", "ClaimProcessor");

                    throw; // Will be caught by outer try/catch
                }
            }

            // Process payment through Finance Service
            using (var paymentActivity = TraceHelper.CreateNestedSpan("PaymentProcessing", "ExternalService",
                new Dictionary<string, object?>
                {
                    ["payment.amount"] = claimRequest.Amount,
                    ["claim.id"] = claimRequest.ClaimId
                }))
            {
                // Retry logic for finance service
                bool paymentSuccess = false;
                string paymentId = string.Empty;
                Exception? lastException = null;

                while (retryCount < 3 && !paymentSuccess)
                {
                    try
                    {
                        if (retryCount > 0)
                        {
                            _logger.LogInformation("Retrying payment processing for claim {ClaimId}, attempt {Attempt}",
                                claimRequest.ClaimId, retryCount + 1);

                            paymentActivity?.AddEvent(new ActivityEvent("PaymentRetry",
                                tags: new ActivityTagsCollection {
                                    { "retry.count", retryCount },
                                    { "retry.delay_ms", retryCount * 200 }
                                }));

                            await Task.Delay(retryCount * 200); // Backoff strategy
                        }

                        // Create HttpClient with automatic distributed tracing
                        var client = _httpClientFactory.CreateClient("FinanceService");

                        var paymentRequest = new
                        {
                            ClaimId = claimRequest.ClaimId,
                            PolicyNumber = claimRequest.PolicyNumber,
                            Amount = claimRequest.Amount
                        };

                        var response = await client.PostAsJsonAsync("http://localhost:1201/api/finance/process-payment", paymentRequest);

                        if (response.IsSuccessStatusCode)
                        {
                            var paymentResult = await response.Content.ReadFromJsonAsync<PaymentResult>();
                            paymentId = paymentResult?.PaymentId ?? Guid.NewGuid().ToString();
                            paymentSuccess = true;

                            paymentActivity?.SetTag("payment.id", paymentId);
                            paymentActivity?.SetTag("payment.success", true);

                            _logger.LogInformation("Payment processed successfully for claim {ClaimId}. Payment ID: {PaymentId}",
                                claimRequest.ClaimId, paymentId);
                        }
                        else
                        {
                            TraceHelper.RecordHttpResponse(response.StatusCode,
                                "http://localhost:1201/api/finance/process-payment",
                                "Finance service error", _logger);

                            var errorContent = await response.Content.ReadAsStringAsync();
                            paymentActivity?.SetTag("payment.error", errorContent);
                            paymentActivity?.SetTag("payment.status_code", (int)response.StatusCode);

                            lastException = new Exception($"Payment failed: {response.StatusCode} - {errorContent}");

                            retryCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        retryCount++;

                        // Record exception but don't stop retrying
                        TraceHelper.RecordException(ex, _logger, "PaymentProcessing", "FinanceService");
                        paymentActivity?.SetTag("error.retry", retryCount);
                    }
                }

                if (!paymentSuccess)
                {
                    processingActivity?.SetTag("claim.payment_failed", true);

                    if (lastException != null)
                    {
                        TraceHelper.RecordException(lastException, _logger, "PaymentProcessing", "FinanceService");
                    }

                    TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.InternalServerError,
                        "/api/claim/process", "Payment processing failed", _logger);

                    result.Status = "Error";
                    result.Notes = "Payment processing failed after retries";
                    return result;
                }

                // Record payment details
                result.PaymentId = paymentId;
                result.Status = "Approved";
                result.ProcessedAmount = claimRequest.Amount;
            }

            // Record success metrics
            stopwatch.Stop();
            _telemetryConfig.SuccessfulRequests.Add(1,
                new KeyValuePair<string, object?>("operation", "ProcessClaim"));

            _telemetryConfig.RequestDuration.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("operation", "ProcessClaim"));

            processingActivity?.SetTag("claim.processing_time_ms", stopwatch.ElapsedMilliseconds);
            processingActivity?.SetTag("claim.approved", true);
            processingActivity?.AddEvent(new ActivityEvent("ClaimProcessingCompleted"));

            _logger.LogInformation(
                "Claim {ClaimId} processed successfully in {ProcessingTimeMs}ms. Payment ID: {PaymentId}",
                claimRequest.ClaimId, stopwatch.ElapsedMilliseconds, result.PaymentId);

            TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.OK,
                "/api/claim/process", "Claim processed successfully", _logger);

            return result;
        }
        catch (Exception ex)
        {
            // Record the exception in the main processing span
            TraceHelper.RecordException(ex, _logger, "ProcessClaim", "ClaimService");

            // Record failure metrics
            stopwatch.Stop();
            _telemetryConfig.FailedRequests.Add(1,
                new KeyValuePair<string, object?>("operation", "ProcessClaim"),
                new KeyValuePair<string, object?>("error", ex.GetType().Name));

            processingActivity?.SetTag("claim.processing_time_ms", stopwatch.ElapsedMilliseconds);
            processingActivity?.SetTag("claim.error", true);
            processingActivity?.AddEvent(new ActivityEvent("ClaimProcessingFailed"));

            result.Status = "Error";
            result.Notes = $"Processing error: {ex.Message}";

            return result;
        }
    }
}

public class ClaimProcessingResult
{
    public string Status { get; set; } = string.Empty;
    public decimal ProcessedAmount { get; set; }
    public string PaymentId { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public class PolicyValidationResult
{
    public bool IsValid { get; set; }
    public bool HasCoverage { get; set; }
    public decimal Coverage { get; set; }
}

public class PaymentResult
{
    public string PaymentId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
}
