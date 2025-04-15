using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Unicore.Common.OpenTelemetry.Configuration;

namespace Unicore.Claim.Service.Services;

public class ClaimProcessorService : IClaimProcessorService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClaimProcessorService> _logger;
    private readonly TelemetryConfig _telemetryConfig;

    public ClaimProcessorService(
        IHttpClientFactory httpClientFactory,
        ILogger<ClaimProcessorService> logger,
        TelemetryConfig telemetryConfig)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _telemetryConfig = telemetryConfig;
    }

    public async Task<ClaimProcessResult> ProcessClaimAsync(ClaimRequest request)
    {
        _logger.LogInformation("Processing claim {ClaimId} for policy {PolicyNumber} with amount {Amount}",
            request.ClaimId, request.PolicyNumber, request.Amount);

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
            _logger.LogError(ex, "Error processing claim {ClaimId}", request.ClaimId);
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

            return paymentResult ?? new PaymentResult("Unknown", false, "Failed to deserialize response");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling finance service for claim {ClaimId}", request.ClaimId);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}

public record PaymentResult(string Status, bool Approved, string Message);
