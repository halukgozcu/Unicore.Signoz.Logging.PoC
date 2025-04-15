namespace Unicore.Policy.Service.Services;

public interface IPolicyService
{
    Task<PolicyValidationResult> ValidatePolicyAsync(string policyNumber);
}

public record PolicyValidationResult(
    bool IsValid,
    bool HasSufficientCoverage,
    decimal AvailableCoverage);
