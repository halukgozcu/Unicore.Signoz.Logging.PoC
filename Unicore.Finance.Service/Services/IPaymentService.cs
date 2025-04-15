namespace Unicore.Finance.Service.Services;

public interface IPaymentService
{
    Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request);
    Task<PolicyValidationResult> ValidatePolicyAsync(string policyNumber);
}

public record PaymentRequest(
    string ClaimId,
    string PolicyNumber,
    decimal Amount);

public record PaymentResult(
    string Status,
    bool Approved,
    string Message);

public record PolicyValidationResult(
    bool IsValid,
    bool HasSufficientCoverage,
    decimal AvailableCoverage);
