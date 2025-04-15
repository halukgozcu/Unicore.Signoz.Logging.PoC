using System.Diagnostics;
using System.Text.Json;
using Unicore.Common.OpenTelemetry.Configuration;

namespace Unicore.Finance.Service.Services;

public class PaymentService : IPaymentService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PaymentService> _logger;
    private readonly TelemetryConfig _telemetryConfig;

    public PaymentService(
        IHttpClientFactory httpClientFactory,
        ILogger<PaymentService> logger,
        TelemetryConfig telemetryConfig)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _telemetryConfig = telemetryConfig;
    }

    public async Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request)
    {
        // Use Meter to create counter instead of accessing non-existent property
        _telemetryConfig.Meter.CreateCounter<long>("app.payment.counter").Add(1);

        // Create a custom activity (span) for payment processing
        using var activity = _telemetryConfig.ActivitySource.StartActivity("ProcessPayment");
        activity?.SetTag("claim.id", request.ClaimId);
        activity?.SetTag("policy.number", request.PolicyNumber);
        activity?.SetTag("payment.amount", request.Amount);

        _logger.LogInformation("Processing payment for claim {ClaimId}, policy {PolicyNumber}, amount {Amount}",
            request.ClaimId, request.PolicyNumber, request.Amount);

        try
        {
            // Call policy service to validate the policy
            var policyResult = await ValidatePolicyAsync(request.PolicyNumber);

            if (!policyResult.IsValid)
            {
                _logger.LogWarning("Payment rejected: Invalid policy {PolicyNumber}", request.PolicyNumber);
                return new PaymentResult("Rejected", false, "Invalid policy");
            }

            // Use the correct property name for the coverage amount - this may vary depending on your actual model
            decimal coverageAmount = 0;

            // If using policyResult.CoverageAmount
            if (policyResult.GetType().GetProperty("CoverageAmount") != null)
            {
                coverageAmount = (decimal)policyResult.GetType().GetProperty("CoverageAmount").GetValue(policyResult);
            }
            // Otherwise try for Coverage property
            else if (policyResult.GetType().GetProperty("Coverage") != null)
            {
                coverageAmount = (decimal)policyResult.GetType().GetProperty("Coverage").GetValue(policyResult);
            }

            if (request.Amount > coverageAmount)
            {
                _logger.LogWarning(
                    "Payment partially approved: Amount {Amount} exceeds coverage {Coverage} for policy {PolicyNumber}",
                    request.Amount, coverageAmount, request.PolicyNumber);
                return new PaymentResult("PartiallyApproved", true,
                    $"Amount exceeds coverage. Approved amount: {coverageAmount}");
            }

            // Simulate payment processing delay
            await Task.Delay(100);

            _logger.LogInformation("Payment approved for claim {ClaimId}, amount {Amount}", request.ClaimId,
                request.Amount);
            return new PaymentResult("Approved", true, "Payment processed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment for claim {ClaimId}", request.ClaimId);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

            return new PaymentResult("Failed", false, $"Error: {ex.Message}");
        }
    }

    public async Task<PolicyValidationResult> ValidatePolicyAsync(string policyNumber)
    {
        using var activity = _telemetryConfig.ActivitySource.StartActivity("CallPolicyService");
        activity?.SetTag("policy.number", policyNumber);

        try
        {
            var client = _httpClientFactory.CreateClient("policy");
            var response = await client.GetAsync($"/api/policy/validate/{policyNumber}");
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var policyResult = JsonSerializer.Deserialize<PolicyValidationResult>(
                responseContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return policyResult ?? new PolicyValidationResult(false, false, 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating policy {PolicyNumber}", policyNumber);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}