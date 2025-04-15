namespace Unicore.Finance.Service.Models;

public class PaymentResult
{
    public string PaymentId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string TransactionReference { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    // Default constructor
    public PaymentResult() { }

    // Constructor to handle the existing usage pattern
    public PaymentResult(string status, bool success, string message)
    {
        PaymentId = status;
        Success = success;
        Message = message;
    }
}
