using System.Diagnostics;
using Unicore.Common.OpenTelemetry.Configurations;
using Unicore.Common.OpenTelemetry.Helpers;
using Unicore.Finance.Service.Models;

namespace Unicore.Finance.Service.Services;

public class PaymentService : IPaymentService
{
    private readonly ILogger<PaymentService> _logger;
    private readonly TelemetryConfig _telemetryConfig;
    private readonly Random _random = new();

    public PaymentService(ILogger<PaymentService> logger, TelemetryConfig telemetryConfig)
    {
        _logger = logger;
        _telemetryConfig = telemetryConfig;
    }

    public async Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request)
    {
        // Create a main span for payment processing
        using var activity = _telemetryConfig.ActivitySource.StartActivity("ProcessPayment");
        activity?.SetTag("payment.claim_id", request.ClaimId);
        activity?.SetTag("payment.amount", request.Amount);
        activity?.SetTag("payment.policy_number", request.PolicyNumber);
        activity?.AddEvent(new ActivityEvent("PaymentProcessingStarted"));

        var stopwatch = Stopwatch.StartNew();

        // Increment metrics
        _telemetryConfig.RequestCounter.Add(1,
            new KeyValuePair<string, object?>("operation", "ProcessPayment"),
            new KeyValuePair<string, object?>("payment.amount_range", GetAmountRange(request.Amount)));

        _logger.LogInformation(
            "Processing payment of {Amount:C} for claim {ClaimId}, policy {PolicyNumber}",
            request.Amount, request.ClaimId, request.PolicyNumber);

        try
        {
            // Simulate validation checks with child span
            using (var validationActivity = TraceHelper.CreateNestedSpan("PaymentValidation", "BusinessLogic",
                new Dictionary<string, object?>
                {
                    ["payment.claim_id"] = request.ClaimId,
                    ["payment.amount"] = request.Amount
                }))
            {
                // Simulate validation delay
                await Task.Delay(30);

                // Simulate validation error (1 in 15 chance)
                if (_random.Next(15) == 0)
                {
                    TraceHelper.RecordWarning(_logger,
                        "Payment validation failed",
                        "Amount exceeds single payment threshold",
                        "PaymentValidator",
                        new Dictionary<string, object>
                        {
                            ["payment.amount"] = request.Amount,
                            ["payment.threshold"] = 5000m,
                            ["payment.exceeded_by"] = request.Amount > 5000m ? (request.Amount - 5000m) : 0
                        });

                    validationActivity?.SetTag("validation.passed", false);
                    validationActivity?.SetTag("validation.error", "amount_exceeds_threshold");

                    activity?.SetTag("payment.rejected", true);
                    activity?.SetTag("payment.rejection_reason", "Amount exceeds threshold");

                    TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.BadRequest,
                        "/api/finance/process-payment", "Payment amount exceeds threshold", _logger);

                    return new PaymentResult
                    {
                        Success = false,
                        Message = "Payment amount exceeds single payment threshold"
                    };
                }

                validationActivity?.SetTag("validation.passed", true);
                validationActivity?.AddEvent(new ActivityEvent("ValidationPassed"));
            }

            // Simulate fund availability check
            using (var fundsActivity = TraceHelper.CreateNestedSpan("FundAvailabilityCheck", "BusinessLogic",
                new Dictionary<string, object?>
                {
                    ["payment.amount"] = request.Amount
                }))
            {
                await Task.Delay(40);

                // Simulate insufficient funds (1 in 20 chance)
                if (_random.Next(20) == 0)
                {
                    TraceHelper.RecordWarning(_logger,
                        "Insufficient funds for payment",
                        "Available funds are below required amount",
                        "FundsChecker",
                        new Dictionary<string, object>
                        {
                            ["payment.amount"] = request.Amount,
                            ["funds.available"] = request.Amount * 0.8m,
                            ["funds.shortfall"] = request.Amount * 0.2m
                        });

                    fundsActivity?.SetTag("funds.sufficient", false);
                    fundsActivity?.SetTag("funds.available", request.Amount * 0.8m);
                    fundsActivity?.SetTag("funds.shortfall", request.Amount * 0.2m);

                    activity?.SetTag("payment.rejected", true);
                    activity?.SetTag("payment.rejection_reason", "Insufficient funds");

                    TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.BadRequest,
                        "/api/finance/process-payment", "Insufficient funds for payment", _logger);

                    return new PaymentResult
                    {
                        Success = false,
                        Message = "Insufficient funds to process payment"
                    };
                }

                fundsActivity?.SetTag("funds.sufficient", true);
                fundsActivity?.AddEvent(new ActivityEvent("FundsAvailable"));
            }

            // Simulate payment gateway interaction with possible errors
            using (var gatewayActivity = TraceHelper.CreateNestedSpan("PaymentGatewayRequest", "ExternalService",
                new Dictionary<string, object?>
                {
                    ["payment.amount"] = request.Amount,
                    ["payment.gateway"] = "MockPaymentProcessor"
                }))
            {
                // Simulate external payment gateway call
                await Task.Delay(100 + _random.Next(100)); // Variable gateway response time

                // Simulate gateway timeout or error (1 in 10 chance)
                if (_random.Next(10) == 0)
                {
                    // Determine if it's a timeout or other error
                    if (_random.Next(2) == 0)
                    {
                        // Gateway timeout
                        var timeoutException = new TimeoutException("Payment gateway response timeout after 30 seconds");
                        TraceHelper.RecordException(timeoutException, _logger, "PaymentGatewayRequest", "ExternalGateway");

                        gatewayActivity?.SetTag("error.timeout", true);
                        gatewayActivity?.SetTag("error.timeout_seconds", 30);

                        activity?.SetTag("payment.error", "gateway_timeout");

                        TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.GatewayTimeout,
                            "https://payment-gateway.example.com/process", "Gateway timeout", _logger);

                        return new PaymentResult
                        {
                            Success = false,
                            Message = "Payment gateway timeout"
                        };
                    }
                    else
                    {
                        // Gateway error
                        var gatewayException = new InvalidOperationException("Payment gateway rejected transaction: Insufficient funds");
                        TraceHelper.RecordException(gatewayException, _logger, "PaymentGatewayRequest", "ExternalGateway");

                        gatewayActivity?.SetTag("gateway.error_code", "INSUFFICIENT_FUNDS");
                        gatewayActivity?.SetTag("gateway.transaction_id", Guid.NewGuid().ToString());

                        activity?.SetTag("payment.error", "gateway_rejection");

                        TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.BadRequest,
                            "https://payment-gateway.example.com/process", "Gateway rejected payment", _logger);

                        return new PaymentResult
                        {
                            Success = false,
                            Message = "Payment gateway rejected the transaction"
                        };
                    }
                }

                // Generate transaction reference 
                var transactionId = Guid.NewGuid().ToString();

                gatewayActivity?.SetTag("gateway.transaction_id", transactionId);
                gatewayActivity?.SetTag("gateway.status", "approved");
                gatewayActivity?.SetTag("gateway.response_time_ms", gatewayActivity.Duration.TotalMilliseconds);
                gatewayActivity?.AddEvent(new ActivityEvent("GatewayTransactionComplete"));

                // Record payment transaction in database
                using (var dbActivity = TraceHelper.CreateNestedSpan("PaymentRecordSave", "Database",
                    new Dictionary<string, object?>
                    {
                        ["db.operation"] = "insert",
                        ["payment.claim_id"] = request.ClaimId,
                        ["payment.transaction_id"] = transactionId
                    }))
                {
                    // Simulate database insert
                    await Task.Delay(30);

                    // Simulate rare database error (1 in 25 chance)
                    if (_random.Next(25) == 0)
                    {
                        var dbException = new Exception("Database constraint violation: Duplicate payment ID");
                        TraceHelper.RecordException(dbException, _logger, "DatabaseOperation", "PaymentDatabase");

                        dbActivity?.SetTag("db.error", true);
                        dbActivity?.SetTag("db.error_code", "CONSTRAINT_VIOLATION");

                        activity?.SetTag("payment.error", "database_error");

                        throw dbException; // This will be caught by outer try/catch
                    }

                    dbActivity?.SetTag("db.record_id", Guid.NewGuid().ToString());
                    dbActivity?.SetTag("db.success", true);
                    dbActivity?.AddEvent(new ActivityEvent("DatabaseRecordCreated"));
                }

                // Generate payment ID
                var paymentId = $"PAY-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8]}";

                // Record success metrics
                stopwatch.Stop();
                _telemetryConfig.SuccessfulRequests.Add(1,
                    new KeyValuePair<string, object?>("operation", "ProcessPayment"));

                _telemetryConfig.RequestDuration.Record(stopwatch.ElapsedMilliseconds,
                    new KeyValuePair<string, object?>("operation", "ProcessPayment"),
                    new KeyValuePair<string, object?>("payment.amount_range", GetAmountRange(request.Amount)));

                activity?.SetTag("payment.processing_time_ms", stopwatch.ElapsedMilliseconds);
                activity?.SetTag("payment.id", paymentId);
                activity?.SetTag("payment.success", true);
                activity?.SetTag("payment.transaction_id", transactionId);
                activity?.AddEvent(new ActivityEvent("PaymentProcessingCompleted"));

                _logger.LogInformation(
                    "Payment {PaymentId} processed successfully in {ProcessingTimeMs}ms for claim {ClaimId}. " +
                    "Transaction ID: {TransactionId}",
                    paymentId, stopwatch.ElapsedMilliseconds, request.ClaimId, transactionId);

                TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.OK,
                    "/api/finance/process-payment", "Payment processed successfully", _logger);

                return new PaymentResult
                {
                    PaymentId = paymentId,
                    Success = true,
                    TransactionReference = transactionId
                };
            }
        }
        catch (Exception ex)
        {
            // Record the exception in the main processing span
            TraceHelper.RecordException(ex, _logger, "ProcessPayment", "PaymentService");

            // Record failure metrics
            stopwatch.Stop();
            _telemetryConfig.FailedRequests.Add(1,
                new KeyValuePair<string, object?>("operation", "ProcessPayment"),
                new KeyValuePair<string, object?>("error", ex.GetType().Name));

            activity?.SetTag("payment.processing_time_ms", stopwatch.ElapsedMilliseconds);
            activity?.SetTag("payment.error", true);
            activity?.AddEvent(new ActivityEvent("PaymentProcessingFailed"));

            _logger.LogError(ex,
                "Payment processing failed for claim {ClaimId}: {ErrorMessage}",
                request.ClaimId, ex.Message);

            TraceHelper.RecordHttpResponse(System.Net.HttpStatusCode.InternalServerError,
                "/api/finance/process-payment", $"Payment processing error: {ex.Message}", _logger);

            return new PaymentResult
            {
                Success = false,
                Message = $"Payment processing error: {ex.Message}"
            };
        }
    }

    private string GetAmountRange(decimal amount)
    {
        return amount switch
        {
            <= 100 => "small",
            <= 1000 => "medium",
            <= 5000 => "large",
            _ => "very_large"
        };
    }
}