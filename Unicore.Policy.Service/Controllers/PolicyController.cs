using Microsoft.AspNetCore.Mvc;
using Unicore.Policy.Service.Services;

namespace Unicore.Policy.Service.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PolicyController : ControllerBase
{
    private readonly IPolicyService _policyService;
    private readonly ILogger<PolicyController> _logger;

    public PolicyController(IPolicyService policyService, ILogger<PolicyController> logger)
    {
        _policyService = policyService;
        _logger = logger;
    }

    [HttpGet("validate/{policyNumber}")]
    public async Task<IActionResult> ValidatePolicy(string policyNumber)
    {
        _logger.LogInformation("Received policy validation request for {PolicyNumber}", policyNumber);

        try
        {
            var result = await _policyService.ValidatePolicyAsync(policyNumber);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating policy {PolicyNumber}", policyNumber);
            return StatusCode(500, new { Error = "An error occurred while validating the policy" });
        }
    }

    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        _logger.LogInformation("Health check requested");
        return Ok(new { Status = "Healthy", Service = "Policy Service" });
    }
}
