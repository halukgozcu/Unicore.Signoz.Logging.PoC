using Microsoft.AspNetCore.Mvc;
using Unicore.Finance.Service.Services;

namespace Unicore.Finance.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(IPaymentService paymentService, ILogger<PaymentController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    [HttpPost("process")]
    public async Task<IActionResult> ProcessPayment([FromBody] PaymentRequest request)
    {
        _logger.LogInformation("Received payment process request for claim {ClaimId}", request.ClaimId);

        try
        {
            var result = await _paymentService.ProcessPaymentAsync(request);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing payment for claim {ClaimId}", request.ClaimId);
            return StatusCode(500, new { Error = "An error occurred while processing the payment" });
        }
    }

    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        _logger.LogInformation("Health check requested");
        return Ok(new { Status = "Healthy", Service = "Finance Service" });
    }
}
