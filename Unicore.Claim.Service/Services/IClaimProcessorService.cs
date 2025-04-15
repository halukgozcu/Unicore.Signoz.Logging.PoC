namespace Unicore.Claim.Service.Services;

public interface IClaimProcessorService
{
    Task<ClaimProcessResult> ProcessClaimAsync(ClaimRequest request);
}

public record ClaimRequest(
    string ClaimId,
    string PolicyNumber,
    decimal Amount,
    string Description);

public record ClaimProcessResult(
    string ClaimId,
    string Status,
    bool IsApproved,
    decimal ApprovedAmount,
    string? Message = null);
