using Unicore.Common.OpenTelemetry.Configuration;

namespace Unicore.Policy.Service.Services;

public class PolicyManagementService : IPolicyService
{
    private readonly ILogger<PolicyManagementService> _logger;
    private readonly TelemetryConfig _telemetryConfig;

    // Mock data for demo purposes
    private readonly Dictionary<string, decimal> _policyCoverages = new()
    {
        { "POL-12345", 5000m },
        { "POL-67890", 10000m },
        { "POL-54321", 7500m },
    };

    public PolicyManagementService(ILogger<PolicyManagementService> logger, TelemetryConfig telemetryConfig)
    {
        _logger = logger;
        _telemetryConfig = telemetryConfig;
    }

    public async Task<PolicyValidationResult> ValidatePolicyAsync(string policyNumber)
    {
        _logger.LogInformation("Validating policy {PolicyNumber}", policyNumber);

        // Increment our custom counter metric
        _telemetryConfig.Meter.CreateCounter<long>("app.policy.counter").Add(1);

        // Create a custom span for policy validation
        using var activity = _telemetryConfig.ActivitySource.StartActivity("ValidatePolicy");
        activity?.SetTag("policy.number", policyNumber);

        // Simulate database lookup
        await Task.Delay(100);

        var isValid = _policyCoverages.ContainsKey(policyNumber);
        var coverage = isValid ? _policyCoverages[policyNumber] : 0m;

        activity?.SetTag("policy.valid", isValid);
        activity?.SetTag("policy.coverage", coverage);

        if (!isValid)
        {
            _logger.LogWarning("Invalid policy number: {PolicyNumber}", policyNumber);
            return new PolicyValidationResult(false, false, 0);
        }

        _logger.LogInformation("Policy {PolicyNumber} is valid with coverage {Coverage}",
            policyNumber, coverage);

        return new PolicyValidationResult(true, coverage > 0, coverage);
    }
}
