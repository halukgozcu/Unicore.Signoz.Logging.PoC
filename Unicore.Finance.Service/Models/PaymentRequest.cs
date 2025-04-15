namespace Unicore.Finance.Service.Models;

public class PaymentRequest
{
    public string ClaimId { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PayeeId { get; set; } = string.Empty;
}
