using Unicore.Claim.Service.Models;

namespace Unicore.Claim.Service.Services;

public interface IClaimProcessorService
{
    Task<ClaimProcessingResult> ProcessClaimAsync(ClaimRequest claimRequest);
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
