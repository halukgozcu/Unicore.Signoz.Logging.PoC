using Unicore.Finance.Service.Models;

namespace Unicore.Finance.Service.Services;

public interface IPaymentService
{
    Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request);
}

public record PaymentRequest(
    string ClaimId,
    string PolicyNumber,
    decimal Amount);

public record PolicyValidationResult(
    bool IsValid,
    bool HasSufficientCoverage,
    decimal AvailableCoverage);
