namespace Unicore.Claim.Service.Models;

public class ClaimRequest
{
    public string ClaimId { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string ProcessingType { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
}
