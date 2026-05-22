using FinSight.Data;
using FinSight.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe.Checkout;

namespace FinSight.Controllers
{
    public class SubscriptionController : Controller
    {
        private readonly FinSightDbContext _context;
        private readonly AuditLogService _auditLog;

        public SubscriptionController(FinSightDbContext context, AuditLogService auditLog)
        {
            _context = context;
            _auditLog = auditLog;
        }

        // ── PENDING: Show subscription required page ──────────
        [HttpGet]
        public IActionResult Pending()
        {
            var userId = HttpContext.Session.GetInt32("UserID");
            if (userId == null) return RedirectToAction("Login", "Auth");

            var subscriptionStatus = HttpContext.Session.GetString("SubscriptionStatus");
            if (subscriptionStatus == "Active")
            {
                return RedirectToAction("Index", "Dashboard"); // Already active
            }

            return View();
        }

        // ── CHECKOUT: Create Stripe session and redirect ──────
        [HttpGet]
        public async Task<IActionResult> Checkout()
        {
            var tenantId = HttpContext.Session.GetInt32("TenantID");
            if (tenantId == null) return RedirectToAction("Login", "Auth");

            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantID == tenantId);
            if (tenant == null) return NotFound();

            if (tenant.SubscriptionStatus == "Active")
            {
                return RedirectToAction("Index", "Dashboard"); // Already active
            }

            // Map SubscriptionPlan to Stripe Price IDs
            string priceId = tenant.SubscriptionPlan switch
            {
                "Basic" => "price_1TUAcYC7yBdjoJdpkeoqx0QB",
                "Premium" => "price_1TUAg3C7yBdjoJdpG3ThG3js",
                "Enterprise" => "price_1TUAhTC7yBdjoJdppwUqRdQ7",
                _ => "price_1TUAcYC7yBdjoJdpkeoqx0QB"
            };

            var domain = $"{Request.Scheme}://{Request.Host}";

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = priceId,
                        Quantity = 1,
                    },
                },
                Mode = "subscription",
                SuccessUrl = domain + "/Subscription/Success?session_id={CHECKOUT_SESSION_ID}",
                CancelUrl = domain + "/Subscription/Cancel",
                ClientReferenceId = tenant.TenantID.ToString(),
                Metadata = new Dictionary<string, string>
                {
                    { "TenantID", tenant.TenantID.ToString() },
                    { "UserID", HttpContext.Session.GetInt32("UserID")?.ToString() ?? "0" },
                    { "Plan", tenant.SubscriptionPlan ?? "Basic" }
                }
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            return Redirect(session.Url);
        }

        [HttpGet]
        public async Task<IActionResult> Success(string session_id)
        {
            var service = new SessionService();
            if (await service.GetAsync(session_id) is not { } session)
            {
                return RedirectToAction(nameof(Cancel));
            }

            var tenantId = HttpContext.Session.GetInt32("TenantID");

            // ── Session Recovery via Stripe Metadata ──
            if (tenantId == null && session.Metadata.ContainsKey("UserID"))
            {
                if (int.TryParse(session.Metadata["UserID"], out int userId))
                {
                    var user = await _context.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.UserID == userId);
                    if (user != null)
                    {
                        HttpContext.Session.SetInt32("UserID", user.UserID);
                        HttpContext.Session.SetString("FullName", user.FullName);
                        HttpContext.Session.SetString("Email", user.Email);
                        HttpContext.Session.SetInt32("TenantID", user.TenantID ?? 0);
                        HttpContext.Session.SetString("CompanyName", user.Tenant?.CompanyName ?? "");
                        HttpContext.Session.SetString("SubscriptionPlan", user.Tenant?.SubscriptionPlan ?? "Basic");
                        HttpContext.Session.SetInt32("RoleID", user.RoleID ?? 1);
                        HttpContext.Session.SetString("RoleName", FinSight.Helpers.Roles.GetRoleName(user.RoleID ?? 1));
                        
                        tenantId = user.TenantID;
                    }
                }
            }

            if (tenantId == null) return RedirectToAction("Login", "Auth");

            // ── Duplicate Payment Prevention ──
            bool alreadyProcessed = await _context.Payments
                .AnyAsync(p => p.StripeSessionID == session_id);

            if (alreadyProcessed)
            {
                HttpContext.Session.SetString("SubscriptionStatus", "Active");
                TempData["SuccessMessage"] = "Payment already processed. Your subscription is active.";
                return RedirectToAction("Index", "Dashboard");
            }

            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.TenantID == tenantId);
            if (tenant == null) return NotFound();

            // ── Tenant Ownership Validation ──
            if (session.ClientReferenceId != tenantId.Value.ToString())
            {
                return RedirectToAction(nameof(Cancel));
            }

            if (session.PaymentStatus == "paid")
            {
                // Update tenant record with Stripe details
                tenant.StripeCustomerId = session.CustomerId;
                tenant.StripeSubscriptionId = session.SubscriptionId;
                tenant.SubscriptionStatus = "Active";

                _context.Update(tenant);

                // ── Create Subscription record ──
                // Find matching Plan by name
                var plan = await _context.Plans
                    .FirstOrDefaultAsync(p => p.PlanName == tenant.SubscriptionPlan && p.IsActive);

                int planId = plan?.PlanID ?? 1;
                int durationMonths = plan?.DurationInMonths ?? 1;
                decimal planPrice = plan?.Price ?? 0;

                var subscription = new FinSight.Models.Subscription
                {
                    TenantID = tenant.TenantID,
                    PlanID = planId,
                    StripeSubscriptionID = session.SubscriptionId,
                    StartDate = DateTime.Now,
                    EndDate = DateTime.Now.AddMonths(durationMonths),
                    Status = "Active",
                    CreatedAt = DateTime.Now
                };

                _context.Subscriptions.Add(subscription);
                await _context.SaveChangesAsync(); // Save to get SubscriptionID

                // ── Create Payment record ──
                var payment = new FinSight.Models.Payment
                {
                    TenantID = tenant.TenantID,
                    SubscriptionID = subscription.SubscriptionID,
                    Amount = planPrice,
                    Currency = "USD",
                    PaymentMethod = "Stripe",
                    PaymentStatus = "Paid",
                    StripeSessionID = session_id,
                    StripePaymentIntentID = session.PaymentIntentId,
                    PaymentDate = DateTime.Now,
                    CreatedAt = DateTime.Now
                };

                _context.Payments.Add(payment);
                await _context.SaveChangesAsync();

                // Audit: Subscription activated + Payment
                await _auditLog.LogSystemAction(tenant.TenantID, null,
                    "SubscriptionActivated", $"Subscription activated for '{tenant.CompanyName}' on plan '{tenant.SubscriptionPlan}'.",
                    HttpContext.Connection.RemoteIpAddress?.ToString());
                await _auditLog.LogSystemAction(tenant.TenantID, null,
                    "PaymentProcessed", $"Stripe payment of {planPrice:C2} processed. Session: {session_id}",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                // Update the session so the filter allows access immediately
                HttpContext.Session.SetString("SubscriptionStatus", "Active");

                TempData["SuccessMessage"] = "Payment successful! Your subscription is now active.";
                return RedirectToAction("Index", "Dashboard");
            }

            // Payment not confirmed — send to cancel
            return RedirectToAction(nameof(Cancel));
        }

        // ── CANCEL: User cancelled the Stripe checkout ────────
        [HttpGet]
        public IActionResult Cancel()
        {
            var userId = HttpContext.Session.GetInt32("UserID");
            if (userId == null) return RedirectToAction("Login", "Auth");

            return View();
        }
    }
}
