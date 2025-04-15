using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Unicore.Common.OpenTelemetry.Configuration;
using Unicore.Claim.Service.Models;

namespace Unicore.Claim.Service.Services;

public class ClaimProcessorService : IClaimProcessorService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClaimProcessorService> _logger;
    private readonly TelemetryConfig _telemetryConfig;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClaimProcessorService(
        IHttpClientFactory httpClientFactory,
        ILogger<ClaimProcessorService> logger,
        TelemetryConfig telemetryConfig,
        IHttpContextAccessor httpContextAccessor)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _telemetryConfig = telemetryConfig;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<ClaimProcessResult> ProcessClaimAsync(ClaimRequest request)
    {

        _logger.LogDebug(
            "Processing claim {ClaimId} with amount {Amount}",
            request.ClaimId, request.Amount);
        // Increment our custom counter metric
        _telemetryConfig.RequestCounter.Add(1);

        // Create a custom activity (span) for claim validation
        using var activity = _telemetryConfig.ActivitySource.StartActivity("ValidateClaimDetails");
        activity?.SetTag("claim.id", request.ClaimId);
        activity?.SetTag("policy.number", request.PolicyNumber);
        activity?.SetTag("claim.amount", request.Amount);

        // Simulate some validation work
        await Task.Delay(100);

        try
        {
            // Make a call to Finance service for payment processing
            var paymentResult = await ProcessPaymentAsync(request);

            _logger.LogInformation("Claim {ClaimId} processed successfully with payment status: {Status}",
                request.ClaimId, paymentResult.Status);

            return new ClaimProcessResult(
                request.ClaimId,
                "Processed",
                paymentResult.Approved,
                paymentResult.Approved ? request.Amount : 0,
                paymentResult.Message
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process claim {ClaimId}. Error Type: {ErrorType}, Error Code: {ErrorCode}, Component: {Component}. " +
                "Correlation ID: {CorrelationId}, Attempt: {AttemptNumber}",
                request.ClaimId,
                ex.GetType().Name,
                "UnknownErrorCode",
                "ClaimProcessor",
                Activity.Current?.TraceId.ToString() ?? "no-trace-id",
                1);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            return new ClaimProcessResult(
                request.ClaimId,
                "Failed",
                false,
                0,
                $"Error: {ex.Message}"
            );
        }
    }

    private async Task<PaymentResult> ProcessPaymentAsync(ClaimRequest request)
    {
        using var activity = _telemetryConfig.ActivitySource.StartActivity("CallFinanceService");
        activity?.SetTag("claim.id", request.ClaimId);

        try
        {
            var client = _httpClientFactory.CreateClient("finance");

            var paymentRequest = new
            {
                ClaimId = request.ClaimId,
                PolicyNumber = request.PolicyNumber,
                Amount = request.Amount
            };

            var content = new StringContent(
                JsonSerializer.Serialize(paymentRequest),
                Encoding.UTF8,
                "application/json");

            var response = await client.PostAsync("/api/payment/process", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var paymentResult = JsonSerializer.Deserialize<PaymentResult>(
                responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogDebug(
                "Claim {ClaimId} processing metrics - Duration: {ProcessingTimeMs}ms, " +
                "Database Calls: {DbCalls}, Cache Hits: {CacheHits}, Cache Misses: {CacheMisses}, " +
                "Memory Usage: {MemoryUsageMB}MB",
                request.ClaimId,
                100, // Simulated processing time
                5,   // Simulated database calls
                3,   // Simulated cache hits
                2,   // Simulated cache misses
                50); // Simulated memory usage

            return paymentResult ?? new PaymentResult("Unknown", false, "Failed to deserialize response");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process claim {ClaimId}. Error Type: {ErrorType}, Error Code: {ErrorCode}, Component: {Component}. " +
                "Correlation ID: {CorrelationId}, Attempt: {AttemptNumber}",
                request.ClaimId,
                ex.GetType().Name,
                "UnknownErrorCode",
                "FinanceService",
                Activity.Current?.TraceId.ToString() ?? "no-trace-id",
                1);

            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}

public record PaymentResult(string Status, bool Approved, string Message);
