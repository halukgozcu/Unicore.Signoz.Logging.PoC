using System.Diagnostics;
using Unicore.Common.OpenTelemetry.Configuration;
using Unicore.Common.OpenTelemetry.Helpers;

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
        _telemetryConfig.RequestCounter.Add(1, new KeyValuePair<string, object?>("operation", "ValidatePolicy"));

        // Create a custom span for policy validation
        using var activity = _telemetryConfig.ActivitySource.StartActivity("PolicyValidation");
        activity?.SetTag("policy.number", policyNumber);
        activity?.SetTag("policy.operation", "validate");
        activity?.AddEvent(new ActivityEvent("PolicyValidationStarted"));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Create a child span for the database lookup
            using (var dbActivity = TraceHelper.CreateNestedSpan("PolicyDatabaseLookup", "Database",
                new Dictionary<string, object?>
                {
                    ["db.operation"] = "lookup",
                    ["db.policy_id"] = policyNumber
                }))
            {
                // Simulate database lookup with occasional latency
                var randomDelay = new Random().Next(50, 200);
                await Task.Delay(randomDelay);

                if (randomDelay > 150)
                {
                    // Record slow query warning
                    TraceHelper.RecordWarning(_logger,
                        "Slow database query detected",
                        $"Query took {randomDelay}ms, exceeding the 150ms threshold",
                        "PolicyDatabase",
                        new Dictionary<string, object>
                        {
                            ["query.duration_ms"] = randomDelay,
                            ["policy.number"] = policyNumber
                        });
                }

                dbActivity?.AddEvent(new ActivityEvent("DatabaseQueryComplete"));
            }

            var isValid = _policyCoverages.ContainsKey(policyNumber);
            var coverage = isValid ? _policyCoverages[policyNumber] : 0m;

            activity?.SetTag("policy.valid", isValid);
            activity?.SetTag("policy.coverage", coverage);

            if (!isValid)
            {
                // Randomly simulate two types of policy errors
                if (new Random().Next(0, 10) > 5)
                {
                    // Simulate an expired policy
                    TraceHelper.RecordWarning(_logger,
                        "Policy validation failed",
                        "Policy has expired",
                        "PolicyValidator",
                        new Dictionary<string, object>
                        {
                            ["policy.number"] = policyNumber,
                            ["policy.status"] = "expired",
                            ["policy.error_code"] = "POL-EXP-001"
                        });

                    TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.BadRequest,
                        "/policies/validate",
                        "Policy has expired",
                        _logger);

                    activity?.SetTag("policy.error", "expired");
                }
                else
                {
                    // Simulate a non-existent policy
                    _logger.LogWarning("Invalid policy number: {PolicyNumber}", policyNumber);

                    TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.NotFound,
                        "/policies/validate",
                        "Policy not found",
                        _logger);

                    activity?.SetTag("policy.error", "not_found");
                }

                return new PolicyValidationResult(false, false, 0);
            }

            // Randomly simulate coverage check failure
            if (new Random().Next(0, 20) == 1)
            {
                // Simulate a database connection exception during coverage check
                using var coverageActivity = TraceHelper.CreateNestedSpan("PolicyCoverageCheck",
                    "BusinessLogic",
                    new Dictionary<string, object?> { ["policy.number"] = policyNumber });

                var exception = new InvalidOperationException("Failed to retrieve coverage details from database");
                TraceHelper.RecordException(exception, _logger, "CoverageCheck", "PolicyDatabase");

                throw exception;
            }

            _logger.LogInformation("Policy {PolicyNumber} is valid with coverage {Coverage}",
                policyNumber, coverage);

            // Record policy details
            activity?.SetTag("policy.premium_tier", coverage > 7500m ? "premium" : "standard");

            // Create a child span for eligibility check
            using (var eligibilityActivity = TraceHelper.CreateNestedSpan("PolicyEligibilityCheck",
                "BusinessLogic",
                new Dictionary<string, object?> { ["policy.number"] = policyNumber }))
            {
                // Simulate eligibility check
                await Task.Delay(50);
                eligibilityActivity?.SetTag("eligibility.status", "approved");
                eligibilityActivity?.AddEvent(new ActivityEvent("EligibilityCheckComplete"));
            }

            // Record performance metrics for this operation
            stopwatch.Stop();
            _telemetryConfig.RequestDuration.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("operation", "ValidatePolicy"),
                new KeyValuePair<string, object?>("policy.number", policyNumber));

            activity?.AddEvent(new ActivityEvent("PolicyValidationCompleted"));

            return new PolicyValidationResult(true, coverage > 0, coverage);
        }
        catch (Exception ex)
        {
            // Record the exception in the current span
            TraceHelper.RecordException(ex, _logger, "PolicyValidation", "PolicyService");

            // Record failed metrics
            _telemetryConfig.FailedRequests.Add(1,
                new KeyValuePair<string, object?>("operation", "ValidatePolicy"),
                new KeyValuePair<string, object?>("error", ex.GetType().Name));

            activity?.AddEvent(new ActivityEvent("PolicyValidationFailed"));

            throw; // Re-throw to propagate to caller
        }
    }
}
