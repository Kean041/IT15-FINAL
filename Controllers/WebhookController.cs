using FinSight.Data;
using FinSight.Models;
using FinSight.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using System.IO;

namespace FinSight.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class WebhookController : ControllerBase
    {
        private readonly FinSightDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<WebhookController> _logger;
        private readonly AuditLogService _auditLog;
        private readonly NotificationService _notification;

        public WebhookController(
            FinSightDbContext context,
            IConfiguration configuration,
            ILogger<WebhookController> logger,
            AuditLogService auditLog,
            NotificationService notification)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _auditLog = auditLog;
            _notification = notification;
        }

        [HttpPost("stripe")]
        public async Task<IActionResult> StripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
            var endpointSecret = _configuration["Stripe:WebhookSecret"];

            try
            {
                // Verify the Stripe signature to ensure the request is genuinely from Stripe
                var stripeEvent = EventUtility.ConstructEvent(
                    json,
                    Request.Headers["Stripe-Signature"],
                    endpointSecret
                );

                // Handle specific events
                switch (stripeEvent.Type)
                {
                    case "invoice.payment_succeeded":
                        await HandlePaymentSucceeded(stripeEvent, json);
                        break;

                    case "invoice.payment_failed":
                        await HandlePaymentFailed(stripeEvent, json);
                        break;

                    case "customer.subscription.deleted":
                        await HandleSubscriptionDeleted(stripeEvent);
                        break;

                    default:
                        _logger.LogInformation("Unhandled Stripe webhook event: {Type}", stripeEvent.Type);
                        break;
                }

                return Ok();
            }
            catch (StripeException e)
            {
                _logger.LogError(e, "Stripe signature verification failed.");
                return BadRequest("Invalid Stripe signature.");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Exception processing Stripe webhook.");
                return StatusCode(500, "Internal server error.");
            }
        }

        private async Task HandlePaymentSucceeded(Stripe.Event stripeEvent, string rawJson)
        {
            var invoice = stripeEvent.Data.Object as Stripe.Invoice;
            if (invoice == null) return;

            string? subscriptionId = null;
            string? paymentIntentId = null;

            try {
                using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                var obj = doc.RootElement.GetProperty("data").GetProperty("object");
                if (obj.TryGetProperty("subscription", out var subProp) && subProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    subscriptionId = subProp.GetString();
                if (obj.TryGetProperty("payment_intent", out var piProp) && piProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    paymentIntentId = piProp.GetString();
            } catch { /* ignore */ }

            if (string.IsNullOrEmpty(subscriptionId)) return;

            // Find the subscription in our database
            var subscription = await _context.Subscriptions
                .Include(s => s.Tenant)
                .FirstOrDefaultAsync(s => s.StripeSubscriptionID == subscriptionId);

            if (subscription == null)
            {
                _logger.LogWarning("Payment succeeded for unknown Stripe subscription ID: {Id}", subscriptionId);
                return;
            }

            // Extend subscription end date (assuming monthly for now, or derive from invoice)
            subscription.EndDate = DateTime.Now.AddMonths(1);
            subscription.Status = "Active";
            subscription.UpdatedAt = DateTime.Now;

            // Record the payment
            var payment = new Payment
            {
                TenantID = subscription.TenantID,
                SubscriptionID = subscription.SubscriptionID,
                Amount = (decimal)invoice.AmountPaid / 100, // Stripe amounts are in cents
                Currency = invoice.Currency?.ToUpper() ?? "USD",
                PaymentMethod = "Stripe",
                PaymentStatus = "Paid",
                StripePaymentIntentID = paymentIntentId
            };

            _context.Payments.Add(payment);
            _context.Update(subscription);
            
            // Log System Event
            await _auditLog.LogSystemAction(subscription.TenantID, null,
                "WebhookPaymentSucceeded", $"Webhook recorded successful payment of {payment.Amount:C2} for subscription.",
                "Stripe System");

            // Notify Tenant Admin & Super Admin
            await _notification.CreateNotificationAsync(subscription.TenantID, null, "Payment", 
                "Payment Succeeded", 
                $"Your recent payment of {payment.Amount:C2} was successful. Subscription extended.", 
                "/SuperAdmin/Payments"); // Assuming route for tenant or super admin

            await _notification.CreateNotificationAsync(null, null, "Payment", 
                "Tenant Payment Received", 
                $"Tenant {subscription.TenantID} paid {payment.Amount:C2}.", 
                "/SuperAdmin/Payments");

            await _context.SaveChangesAsync();
            _logger.LogInformation("Successfully processed invoice payment succeeded for Tenant {TenantID}", subscription.TenantID);
        }

        private async Task HandlePaymentFailed(Stripe.Event stripeEvent, string rawJson)
        {
            var invoice = stripeEvent.Data.Object as Stripe.Invoice;
            if (invoice == null) return;

            string? subscriptionId = null;
            string? paymentIntentId = null;

            try {
                using var doc = System.Text.Json.JsonDocument.Parse(rawJson);
                var obj = doc.RootElement.GetProperty("data").GetProperty("object");
                if (obj.TryGetProperty("subscription", out var subProp) && subProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    subscriptionId = subProp.GetString();
                if (obj.TryGetProperty("payment_intent", out var piProp) && piProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    paymentIntentId = piProp.GetString();
            } catch { /* ignore */ }

            if (string.IsNullOrEmpty(subscriptionId)) return;

            var subscription = await _context.Subscriptions
                .Include(s => s.Tenant)
                .FirstOrDefaultAsync(s => s.StripeSubscriptionID == subscriptionId);

            if (subscription == null) return;

            // Update subscription to Past Due or Suspended
            subscription.Status = "Past Due";
            subscription.UpdatedAt = DateTime.Now;
            _context.Update(subscription);

            // Add a failed payment record
            var payment = new Payment
            {
                TenantID = subscription.TenantID,
                SubscriptionID = subscription.SubscriptionID,
                Amount = (decimal)invoice.AmountDue / 100,
                Currency = invoice.Currency?.ToUpper() ?? "USD",
                PaymentMethod = "Stripe",
                PaymentStatus = "Failed",
                StripePaymentIntentID = paymentIntentId
            };

            _context.Payments.Add(payment);
            
            // Log Security Event
            await _auditLog.LogSecurityAction(subscription.TenantID, null,
                "WebhookPaymentFailed", $"Webhook recorded failed payment of {payment.Amount:C2} for subscription.",
                "Stripe System", "Warning");

            // Notify Tenant Admin & Super Admin
            await _notification.CreateNotificationAsync(subscription.TenantID, null, "Payment", 
                "Payment Failed", 
                $"Your recent payment of {payment.Amount:C2} failed. Please update your billing info.", 
                "/SuperAdmin/Payments");

            await _notification.CreateNotificationAsync(null, null, "Payment", 
                "Tenant Payment Failed", 
                $"Tenant {subscription.TenantID} failed payment of {payment.Amount:C2}.", 
                "/SuperAdmin/Payments");

            await _context.SaveChangesAsync();
            _logger.LogWarning("Processed invoice payment failed for Tenant {TenantID}. Subscription is Past Due.", subscription.TenantID);
        }

        private async Task HandleSubscriptionDeleted(Stripe.Event stripeEvent)
        {
            var stripeSub = stripeEvent.Data.Object as Stripe.Subscription;
            if (stripeSub == null) return;

            var subscription = await _context.Subscriptions
                .FirstOrDefaultAsync(s => s.StripeSubscriptionID == stripeSub.Id);

            if (subscription == null) return;

            subscription.Status = "Cancelled";
            subscription.UpdatedAt = DateTime.Now;
            _context.Update(subscription);

            // Log System Event
            await _auditLog.LogSystemAction(subscription.TenantID, null,
                "WebhookSubscriptionCancelled", "Webhook recorded subscription cancellation.",
                "Stripe System");

            // Notify Tenant Admin & Super Admin
            await _notification.CreateNotificationAsync(subscription.TenantID, null, "Subscription", 
                "Subscription Cancelled", 
                $"Your subscription has been cancelled.", 
                "/SuperAdmin/Subscriptions");

            await _notification.CreateNotificationAsync(null, null, "Subscription", 
                "Tenant Subscription Cancelled", 
                $"Tenant {subscription.TenantID} cancelled their subscription.", 
                "/SuperAdmin/Subscriptions");

            await _context.SaveChangesAsync();
            _logger.LogInformation("Processed subscription deleted for Tenant {TenantID}. Subscription Cancelled.", subscription.TenantID);
        }
    }
}
