using FinSight.Data;
using FinSight.Helpers;
using FinSight.Models.ViewModels;
using FinSight.Models;
using FinSight.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinSight.Controllers
{
    public class SuperAdminController : BaseController
    {
        private readonly FinSightDbContext _context;

        public SuperAdminController(FinSightDbContext context)
        {
            _context = context;
        }

        // ─────────────────────────────────────────────
        // Ensure user is strictly Super Admin
        // ─────────────────────────────────────────────
        public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            base.OnActionExecuting(context);

            if (!IsAuthenticated || !HasRole(Roles.SuperAdmin))
            {
                context.Result = RedirectToLogin();
            }
        }

        // ═════════════════════════════════════════════
        // DASHBOARD — Real EF Core analytics
        // ═════════════════════════════════════════════
        public async Task<IActionResult> Dashboard()
        {
            var now = DateTime.Now;
            var monthStart = new DateTime(now.Year, now.Month, 1);

            // ── Load all data into memory (small SaaS dataset) ──
            var tenants = await _context.Tenants.ToListAsync();
            var subscriptions = await _context.Subscriptions
                .Include(s => s.Tenant).Include(s => s.Plan).ToListAsync();
            var payments = await _context.Payments
                .Include(p => p.Tenant)
                .Include(p => p.Subscription).ThenInclude(s => s!.Plan)
                .ToListAsync();

            // ── KPI: Tenants ──
            var totalTenants = tenants.Count;
            var activeTenants = tenants.Count(t => t.SubscriptionStatus == "Active");
            var pendingTenants = tenants.Count(t => t.SubscriptionStatus == "Pending");
            var suspendedTenants = tenants.Count(t => t.SubscriptionStatus == "Suspended");
            var newSignups = tenants.Count(t => t.CreatedDate >= monthStart);

            // ── KPI: Subscriptions ──
            var totalSubs = subscriptions.Count;
            var activeSubs = subscriptions.Count(s => s.Status == "Active");
            var expiredSubs = subscriptions.Count(s => s.Status == "Expired");

            // ── KPI: Payments ──
            var paidPayments = payments.Where(p => p.PaymentStatus == "Paid");
            var totalRevenue = paidPayments.Sum(p => p.Amount);
            var monthlyRevenue = paidPayments.Where(p => p.PaymentDate >= monthStart).Sum(p => p.Amount);
            var totalPaymentCount = payments.Count;
            var failedCount = payments.Count(p => p.PaymentStatus == "Failed");
            var stripeCount = payments.Count(p => p.PaymentMethod == "Stripe");
            var manualCount = payments.Count(p => p.PaymentMethod == "Manual");

            // ── Revenue Chart (last 6 months) ──
            var revenueChart = new List<MonthlyRevenuePoint>();
            for (int i = 5; i >= 0; i--)
            {
                var target = now.AddMonths(-i);
                revenueChart.Add(new MonthlyRevenuePoint
                {
                    Month = target.ToString("MMM yyyy"),
                    Revenue = paidPayments
                        .Where(p => p.PaymentDate.Year == target.Year && p.PaymentDate.Month == target.Month)
                        .Sum(p => p.Amount)
                });
            }

            // ── Subscription by Plan distribution ──
            var subsByPlan = subscriptions
                .Where(s => s.Plan != null)
                .GroupBy(s => s.Plan!.PlanName)
                .Select(g => new PlanDistribution { PlanName = g.Key, Count = g.Count() })
                .OrderByDescending(p => p.Count)
                .ToList();

            var mostPopular = subsByPlan.FirstOrDefault()?.PlanName ?? "N/A";

            // ── Recent Activity ──
            var recentTenants = tenants
                .OrderByDescending(t => t.CreatedDate).Take(5)
                .Select(t => new RecentTenantItem
                {
                    TenantID = t.TenantID,
                    CompanyName = t.CompanyName ?? "Unknown",
                    Status = t.SubscriptionStatus ?? "Pending",
                    Plan = t.SubscriptionPlan ?? "—",
                    CreatedDate = t.CreatedDate
                }).ToList();

            var recentSubs = subscriptions
                .OrderByDescending(s => s.CreatedAt).Take(5)
                .Select(s => new RecentSubscriptionItem
                {
                    SubscriptionID = s.SubscriptionID,
                    CompanyName = s.Tenant?.CompanyName ?? "Unknown",
                    PlanName = s.Plan?.PlanName ?? "N/A",
                    Status = s.Status,
                    StartDate = s.StartDate,
                    EndDate = s.EndDate
                }).ToList();

            var recentPays = payments
                .OrderByDescending(p => p.PaymentDate).Take(5)
                .Select(p => new RecentPaymentItem
                {
                    PaymentID = p.PaymentID,
                    CompanyName = p.Tenant?.CompanyName ?? "Unknown",
                    PlanName = p.Subscription?.Plan?.PlanName ?? "N/A",
                    Amount = p.Amount,
                    Method = p.PaymentMethod,
                    Status = p.PaymentStatus,
                    PaymentDate = p.PaymentDate
                }).ToList();

            var vm = new SuperAdminDashboardViewModel
            {
                TotalTenants = totalTenants,
                ActiveTenants = activeTenants,
                PendingTenants = pendingTenants,
                SuspendedTenants = suspendedTenants,
                TotalSubscriptions = totalSubs,
                ActiveSubscriptions = activeSubs,
                ExpiredSubscriptions = expiredSubs,
                TotalRevenue = totalRevenue,
                MonthlyRevenue = monthlyRevenue,
                TotalPayments = totalPaymentCount,
                FailedPayments = failedCount,
                NewSignupsThisMonth = newSignups,
                RevenueChart = revenueChart,
                SubscriptionsByPlan = subsByPlan,
                MostPopularPlan = mostPopular,
                StripePayments = stripeCount,
                ManualPayments = manualCount,
                RecentTenants = recentTenants,
                RecentSubscriptions = recentSubs,
                RecentPayments = recentPays
            };

            return View(vm);
        }

        // ═════════════════════════════════════════════
        // TENANTS — Real EF Core implementation
        // ═════════════════════════════════════════════
        public async Task<IActionResult> Tenants(string searchString = "", string statusFilter = "", string planFilter = "", int page = 1)
        {
            // ── Load all tenants with their users into memory ──
            // (Tenant count is small for SuperAdmin; avoids SQL OFFSET compatibility issues)
            var allTenantsList = await _context.Tenants
                .Include(t => t.Users)
                .OrderByDescending(t => t.CreatedDate)
                .ToListAsync();

            // ── KPIs (computed from ALL tenants) ──
            var totalTenants = allTenantsList.Count;
            var activeTenants = allTenantsList.Count(t => t.SubscriptionStatus == "Active");
            var suspendedTenants = allTenantsList.Count(t => t.SubscriptionStatus == "Suspended" || t.SubscriptionStatus == "Inactive");
            var pendingTenants = allTenantsList.Count(t => t.SubscriptionStatus == "Pending" || t.SubscriptionStatus == null);
            var firstOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var newThisMonth = allTenantsList.Count(t => t.CreatedDate >= firstOfMonth);

            // ── Search (in-memory) ──
            var filtered = allTenantsList.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchString))
            {
                var search = searchString.Trim().ToLower();
                filtered = filtered.Where(t =>
                    (t.CompanyName != null && t.CompanyName.ToLower().Contains(search)) ||
                    t.Users.Any(u => u.Email.ToLower().Contains(search))
                );
            }

            // ── Filter by Status ──
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                filtered = filtered.Where(t => string.Equals(t.SubscriptionStatus, statusFilter, StringComparison.OrdinalIgnoreCase));
            }

            // ── Filter by Plan ──
            if (!string.IsNullOrWhiteSpace(planFilter))
            {
                filtered = filtered.Where(t => string.Equals(t.SubscriptionPlan, planFilter, StringComparison.OrdinalIgnoreCase));
            }

            // ── Pagination (in-memory) ──
            int pageSize = 10;
            var filteredList = filtered.ToList();
            var filteredCount = filteredList.Count;
            var totalPages = (int)Math.Ceiling(filteredCount / (double)pageSize);
            if (totalPages == 0) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var tenants = filteredList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(t => new TenantRecord
                {
                    TenantID = t.TenantID,
                    CompanyName = t.CompanyName ?? "Unnamed",
                    AdminEmail = t.Users
                        .Where(u => u.RoleID == Roles.Admin)
                        .Select(u => u.Email)
                        .FirstOrDefault() ?? "No admin",
                    SubscriptionPlan = t.SubscriptionPlan ?? "None",
                    Status = t.SubscriptionStatus ?? "Pending",
                    CreatedDate = t.CreatedDate ?? DateTime.MinValue,
                    TotalUsers = t.Users.Count
                })
                .ToList();

            var viewModel = new SuperAdminTenantViewModel
            {
                TotalTenants = totalTenants,
                ActiveTenants = activeTenants,
                SuspendedTenants = suspendedTenants,
                PendingTenants = pendingTenants,
                NewTenantsThisMonth = newThisMonth,
                Tenants = tenants,
                SearchString = searchString,
                StatusFilter = statusFilter,
                PlanFilter = planFilter,
                CurrentPage = page,
                TotalPages = totalPages
            };

            return View(viewModel);
        }

        // ═════════════════════════════════════════════
        // TENANT DETAILS — AJAX endpoint for modal
        // ═════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> TenantDetails(int id)
        {
            var tenant = await _context.Tenants
                .Include(t => t.Users)
                .FirstOrDefaultAsync(t => t.TenantID == id);

            if (tenant == null)
                return NotFound();

            // Find the admin user (RoleID == 1)
            var admin = tenant.Users
                .Where(u => u.RoleID == Roles.Admin)
                .OrderBy(u => u.CreatedAt)
                .FirstOrDefault();

            var vm = new TenantDetailsViewModel
            {
                TenantID = tenant.TenantID,
                CompanyName = tenant.CompanyName ?? "Unnamed",
                SubscriptionPlan = tenant.SubscriptionPlan ?? "None",
                SubscriptionStatus = tenant.SubscriptionStatus ?? "Pending",
                StripeCustomerId = tenant.StripeCustomerId,
                StripeSubscriptionId = tenant.StripeSubscriptionId,
                CreatedDate = tenant.CreatedDate,
                AdminName = admin?.FullName ?? "N/A",
                AdminEmail = admin?.Email ?? "N/A",
                AdminCreatedAt = admin?.CreatedAt,
                TotalUsers = tenant.Users.Count
            };

            return Json(vm);
        }

        // ═════════════════════════════════════════════
        // UPDATE TENANT STATUS — Activate / Suspend / Inactive
        // ═════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTenantStatus(int id, string status)
        {
            // Validate allowed statuses
            var allowedStatuses = new[] { "Active", "Pending", "Suspended", "Inactive" };
            if (!allowedStatuses.Contains(status))
            {
                TempData["ErrorMessage"] = "Invalid status value.";
                return RedirectToAction(nameof(Tenants));
            }

            var tenant = await _context.Tenants.FindAsync(id);
            if (tenant == null)
            {
                TempData["ErrorMessage"] = "Tenant not found.";
                return RedirectToAction(nameof(Tenants));
            }

            // Business rule: cannot activate without a subscription plan
            if (status == "Active" && string.IsNullOrWhiteSpace(tenant.SubscriptionPlan))
            {
                TempData["ErrorMessage"] = $"Cannot activate \"{tenant.CompanyName}\" — no subscription plan selected.";
                return RedirectToAction(nameof(Tenants));
            }

            var previousStatus = tenant.SubscriptionStatus;
            tenant.SubscriptionStatus = status;

            _context.Update(tenant);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Tenant \"{tenant.CompanyName}\" status changed from {previousStatus} → {status}.";
            return RedirectToAction(nameof(Tenants));
        }

        // ═════════════════════════════════════════════
        // REGISTER TENANT (Direct from Dashboard)
        // ═════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterTenant(SuperAdminRegisterTenantViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = "Failed to register tenant. Please ensure all fields are filled correctly.";
                return RedirectToAction(nameof(Tenants));
            }

            // Check if email exists
            bool emailExists = await _context.Users.AnyAsync(u => u.Email == model.AdminEmail);
            if (emailExists)
            {
                TempData["ErrorMessage"] = "A user with this email already exists.";
                return RedirectToAction(nameof(Tenants));
            }

            // 1. Create Tenant
            var tenant = new Tenant
            {
                CompanyName = model.CompanyName,
                CreatedDate = DateTime.Now,
                SubscriptionPlan = model.SubscriptionPlan,
                SubscriptionStatus = model.SubscriptionStatus // E.g., "Active" by default if SA creates it
            };
            
            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync();

            // 2. Create Admin User
            var adminUser = new User
            {
                FullName = model.AdminName,
                Email = model.AdminEmail,
                PasswordHash = PasswordHelper.HashPassword(model.AdminPassword),
                RoleID = Roles.Admin,
                TenantID = tenant.TenantID,
                IsTwoFactorEnabled = true,
                CreatedAt = DateTime.Now
            };

            _context.Users.Add(adminUser);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Successfully registered {tenant.CompanyName} and created admin account.";
            return RedirectToAction(nameof(Tenants));
        }

        // ═════════════════════════════════════════════
        // SUBSCRIPTIONS — Real EF Core implementation
        // ═════════════════════════════════════════════
        public async Task<IActionResult> Subscriptions(string searchString = "", string statusFilter = "", string planFilter = "", string dateRangeFilter = "", int page = 1)
        {
            // Load all subscriptions with Tenant + Plan into memory
            var allSubs = await _context.Subscriptions
                .Include(s => s.Tenant)
                .Include(s => s.Plan)
                .Include(s => s.Payments)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            // ── Auto-expire: mark past-due subscriptions ──
            var now = DateTime.Now;
            foreach (var sub in allSubs.Where(s => s.Status == "Active" && s.EndDate < now))
            {
                sub.Status = "Expired";
                sub.UpdatedAt = now;
                _context.Update(sub);
            }
            await _context.SaveChangesAsync();

            // ── KPIs ──
            var totalTenants = await _context.Tenants.CountAsync();
            var activeSubs = allSubs.Count(s => s.Status == "Active");
            var expiringSoon = allSubs.Count(s => s.Status == "Active" && s.EndDate <= now.AddDays(30));
            var expiredSubs = allSubs.Count(s => s.Status == "Expired");
            var cancelledSubs = allSubs.Count(s => s.Status == "Cancelled");

            // ── Search (in-memory) ──
            var filtered = allSubs.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchString))
            {
                var search = searchString.Trim().ToLower();
                filtered = filtered.Where(s =>
                    (s.Tenant?.CompanyName != null && s.Tenant.CompanyName.ToLower().Contains(search)) ||
                    (s.Plan?.PlanName != null && s.Plan.PlanName.ToLower().Contains(search))
                );
            }

            // ── Filter by Status ──
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                filtered = filtered.Where(s => string.Equals(s.Status, statusFilter, StringComparison.OrdinalIgnoreCase));
            }

            // ── Filter by Plan ──
            if (!string.IsNullOrWhiteSpace(planFilter))
            {
                filtered = filtered.Where(s => s.Plan != null && string.Equals(s.Plan.PlanName, planFilter, StringComparison.OrdinalIgnoreCase));
            }

            // ── Filter by Date Range ──
            if (!string.IsNullOrWhiteSpace(dateRangeFilter))
            {
                filtered = dateRangeFilter switch
                {
                    "Expiring30" => filtered.Where(s => s.Status == "Active" && s.EndDate <= now.AddDays(30)),
                    "RecentlyAdded" => filtered.Where(s => s.CreatedAt >= now.AddDays(-30)),
                    _ => filtered
                };
            }

            // ── Pagination ──
            int pageSize = 10;
            var filteredList = filtered.ToList();
            var filteredCount = filteredList.Count;
            var totalPages = (int)Math.Ceiling(filteredCount / (double)pageSize);
            if (totalPages == 0) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var subscriptions = filteredList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(s => new SubscriptionRecord
                {
                    SubscriptionID = s.SubscriptionID,
                    TenantID = s.TenantID,
                    TenantName = s.Tenant?.CompanyName ?? "Unknown",
                    PlanName = s.Plan?.PlanName ?? "N/A",
                    StartDate = s.StartDate,
                    EndDate = s.EndDate,
                    Status = s.Status,
                    PaymentStatus = s.Payments.OrderByDescending(p => p.PaymentDate).FirstOrDefault()?.PaymentStatus ?? "N/A",
                    StripeSubscriptionID = s.StripeSubscriptionID,
                    CreatedAt = s.CreatedAt
                })
                .ToList();

            var viewModel = new SuperAdminSubscriptionViewModel
            {
                TotalActiveSubscriptions = activeSubs,
                ExpiringSoon = expiringSoon,
                ExpiredSubscriptions = expiredSubs,
                CancelledSubscriptions = cancelledSubs,
                TotalTenants = totalTenants,
                Subscriptions = subscriptions,
                SearchString = searchString,
                StatusFilter = statusFilter,
                PlanFilter = planFilter,
                DateRangeFilter = dateRangeFilter,
                CurrentPage = page,
                TotalPages = totalPages
            };

            // ── Pass data for New Subscription Modal ──
            ViewBag.AvailableTenants = await _context.Tenants.OrderBy(t => t.CompanyName).ToListAsync();
            ViewBag.Plans = await _context.Plans.Where(p => p.IsActive).OrderBy(p => p.Price).ToListAsync();

            return View(viewModel);
        }

        // ═════════════════════════════════════════════
        // CREATE SUBSCRIPTION (MANUAL)
        // ═════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateSubscription(int tenantId, int planId)
        {
            var tenant = await _context.Tenants.FindAsync(tenantId);
            var plan = await _context.Plans.FindAsync(planId);

            if (tenant == null || plan == null)
            {
                TempData["ErrorMessage"] = "Invalid Tenant or Plan selected.";
                return RedirectToAction(nameof(Subscriptions));
            }

            // Check if tenant already has an active subscription
            bool hasActive = await _context.Subscriptions
                .AnyAsync(s => s.TenantID == tenantId && s.Status == "Active");

            if (hasActive)
            {
                TempData["ErrorMessage"] = $"Tenant \"{tenant.CompanyName}\" already has an active subscription.";
                return RedirectToAction(nameof(Subscriptions));
            }

            var sub = new Subscription
            {
                TenantID = tenantId,
                PlanID = planId,
                StartDate = DateTime.Now,
                EndDate = DateTime.Now.AddMonths(plan.DurationInMonths),
                Status = "Active",
                CreatedAt = DateTime.Now
            };

            _context.Subscriptions.Add(sub);
            await _context.SaveChangesAsync();

            // Create a manual payment record
            var payment = new Payment
            {
                TenantID = tenantId,
                SubscriptionID = sub.SubscriptionID,
                Amount = plan.Price,
                Currency = "USD",
                PaymentMethod = "Manual",
                PaymentStatus = "Paid",
                StripeSessionID = "ADMIN_CREATED",
                PaymentDate = DateTime.Now,
                CreatedAt = DateTime.Now
            };

            _context.Payments.Add(payment);

            tenant.SubscriptionStatus = "Active";
            tenant.SubscriptionPlan = plan.PlanName;
            _context.Update(tenant);

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Successfully created {plan.PlanName} subscription for {tenant.CompanyName}.";
            return RedirectToAction(nameof(Subscriptions));
        }

        // ═════════════════════════════════════════════
        // SUBSCRIPTION DETAILS — AJAX endpoint for modal
        // ═════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> SubscriptionDetails(int id)
        {
            var sub = await _context.Subscriptions
                .Include(s => s.Tenant)
                .Include(s => s.Plan)
                .Include(s => s.Payments)
                .FirstOrDefaultAsync(s => s.SubscriptionID == id);

            if (sub == null) return NotFound();

            var vm = new SubscriptionDetailsViewModel
            {
                SubscriptionID = sub.SubscriptionID,
                Status = sub.Status,
                StartDate = sub.StartDate,
                EndDate = sub.EndDate,
                StripeSubscriptionID = sub.StripeSubscriptionID,
                CreatedAt = sub.CreatedAt,
                PlanName = sub.Plan?.PlanName ?? "N/A",
                PlanPrice = sub.Plan?.Price ?? 0,
                DurationInMonths = sub.Plan?.DurationInMonths ?? 1,
                TenantID = sub.TenantID,
                CompanyName = sub.Tenant?.CompanyName ?? "Unknown",
                StripeCustomerId = sub.Tenant?.StripeCustomerId,
                RecentPayments = sub.Payments
                    .OrderByDescending(p => p.PaymentDate)
                    .Take(5)
                    .Select(p => new PaymentSummary
                    {
                        PaymentID = p.PaymentID,
                        Amount = p.Amount,
                        PaymentMethod = p.PaymentMethod,
                        PaymentStatus = p.PaymentStatus,
                        PaymentDate = p.PaymentDate
                    })
                    .ToList()
            };

            return Json(vm);
        }

        // ═════════════════════════════════════════════
        // UPDATE SUBSCRIPTION STATUS
        // ═════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSubscriptionStatus(int id, string status)
        {
            var allowedStatuses = new[] { "Active", "Cancelled", "Expired" };
            if (!allowedStatuses.Contains(status))
            {
                TempData["ErrorMessage"] = "Invalid status value.";
                return RedirectToAction(nameof(Subscriptions));
            }

            var sub = await _context.Subscriptions
                .Include(s => s.Tenant)
                .FirstOrDefaultAsync(s => s.SubscriptionID == id);

            if (sub == null)
            {
                TempData["ErrorMessage"] = "Subscription not found.";
                return RedirectToAction(nameof(Subscriptions));
            }

            // Business rule: cannot re-activate a cancelled subscription
            if (sub.Status == "Cancelled" && status == "Active")
            {
                TempData["ErrorMessage"] = "Cannot re-activate a cancelled subscription. Please create a new one.";
                return RedirectToAction(nameof(Subscriptions));
            }

            var previous = sub.Status;
            sub.Status = status;
            sub.UpdatedAt = DateTime.Now;

            // If cancelling, also update the tenant's subscription status
            if (status == "Cancelled" && sub.Tenant != null)
            {
                sub.Tenant.SubscriptionStatus = "Cancelled";
                _context.Update(sub.Tenant);
            }

            _context.Update(sub);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Subscription #{sub.SubscriptionID} status changed from {previous} → {status}.";
            return RedirectToAction(nameof(Subscriptions));
        }

        // ═════════════════════════════════════════════
        // RENEW SUBSCRIPTION
        // ═════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenewSubscription(int id)
        {
            var sub = await _context.Subscriptions
                .Include(s => s.Plan)
                .Include(s => s.Tenant)
                .FirstOrDefaultAsync(s => s.SubscriptionID == id);

            if (sub == null)
            {
                TempData["ErrorMessage"] = "Subscription not found.";
                return RedirectToAction(nameof(Subscriptions));
            }

            if (sub.Status == "Active" && sub.EndDate > DateTime.Now)
            {
                TempData["ErrorMessage"] = "This subscription is still active and has not expired yet.";
                return RedirectToAction(nameof(Subscriptions));
            }

            // Extend the subscription by plan duration (default: 1 month)
            int months = sub.Plan?.DurationInMonths ?? 1;
            var renewalStart = DateTime.Now;
            sub.StartDate = renewalStart;
            sub.EndDate = renewalStart.AddMonths(months);
            sub.Status = "Active";
            sub.UpdatedAt = DateTime.Now;

            // Update tenant status too
            if (sub.Tenant != null)
            {
                sub.Tenant.SubscriptionStatus = "Active";
                _context.Update(sub.Tenant);
            }

            _context.Update(sub);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Subscription #{sub.SubscriptionID} renewed until {sub.EndDate:MMM dd, yyyy}.";
            return RedirectToAction(nameof(Subscriptions));
        }

        // ═════════════════════════════════════════════
        // PAYMENTS — Full EF Core implementation
        // ═════════════════════════════════════════════
        public async Task<IActionResult> Payments(string searchString = "", string statusFilter = "", string methodFilter = "", string dateRangeFilter = "", int page = 1)
        {
            var allPayments = await _context.Payments
                .Include(p => p.Tenant)
                .Include(p => p.Subscription)
                    .ThenInclude(s => s!.Plan)
                .OrderByDescending(p => p.PaymentDate)
                .ToListAsync();

            // ── KPIs ──
            var totalRevenue = allPayments.Where(p => p.PaymentStatus == "Paid").Sum(p => p.Amount);
            var totalTransactions = allPayments.Count;
            var successfulPayments = allPayments.Count(p => p.PaymentStatus == "Paid");
            var failedPayments = allPayments.Count(p => p.PaymentStatus == "Failed");
            var refundedPayments = allPayments.Count(p => p.PaymentStatus == "Refunded");
            var now = DateTime.Now;
            var monthlyRevenue = allPayments
                .Where(p => p.PaymentStatus == "Paid" && p.PaymentDate.Year == now.Year && p.PaymentDate.Month == now.Month)
                .Sum(p => p.Amount);

            // ── Monthly Revenue Chart Data (last 6 months) ──
            var monthlyRevenueData = new List<MonthlyRevenueSummary>();
            for (int i = 5; i >= 0; i--)
            {
                var targetDate = now.AddMonths(-i);
                var monthPaid = allPayments
                    .Where(p => p.PaymentStatus == "Paid" && p.PaymentDate.Year == targetDate.Year && p.PaymentDate.Month == targetDate.Month);

                monthlyRevenueData.Add(new MonthlyRevenueSummary
                {
                    Month = targetDate.ToString("MMM yyyy"),
                    Revenue = monthPaid.Sum(p => p.Amount),
                    TransactionCount = monthPaid.Count()
                });
            }

            // ── Search ──
            var filtered = allPayments.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(searchString))
            {
                var search = searchString.Trim().ToLower();
                filtered = filtered.Where(p =>
                    (p.Tenant?.CompanyName != null && p.Tenant.CompanyName.ToLower().Contains(search)) ||
                    (p.Subscription?.Plan?.PlanName != null && p.Subscription.Plan.PlanName.ToLower().Contains(search))
                );
            }

            // ── Filter by Status ──
            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                filtered = filtered.Where(p => string.Equals(p.PaymentStatus, statusFilter, StringComparison.OrdinalIgnoreCase));
            }

            // ── Filter by Method ──
            if (!string.IsNullOrWhiteSpace(methodFilter))
            {
                filtered = filtered.Where(p => string.Equals(p.PaymentMethod, methodFilter, StringComparison.OrdinalIgnoreCase));
            }

            // ── Filter by Date Range ──
            if (!string.IsNullOrWhiteSpace(dateRangeFilter))
            {
                filtered = dateRangeFilter switch
                {
                    "Today" => filtered.Where(p => p.PaymentDate.Date == now.Date),
                    "ThisWeek" => filtered.Where(p => p.PaymentDate >= now.AddDays(-7)),
                    "ThisMonth" => filtered.Where(p => p.PaymentDate >= new DateTime(now.Year, now.Month, 1)),
                    "Last90Days" => filtered.Where(p => p.PaymentDate >= now.AddDays(-90)),
                    _ => filtered
                };
            }

            // ── Pagination ──
            int pageSize = 10;
            var filteredList = filtered.ToList();
            var filteredCount = filteredList.Count;
            var totalPages = (int)Math.Ceiling(filteredCount / (double)pageSize);
            if (totalPages == 0) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var payments = filteredList
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new PaymentRecord
                {
                    PaymentID = p.PaymentID,
                    TenantID = p.TenantID,
                    TenantName = p.Tenant?.CompanyName ?? "Unknown",
                    PlanName = p.Subscription?.Plan?.PlanName ?? "N/A",
                    AmountPaid = p.Amount,
                    Currency = p.Currency,
                    PaymentMethod = p.PaymentMethod,
                    Status = p.PaymentStatus,
                    ReferenceID = p.StripePaymentIntentID ?? p.StripeSessionID ?? "—",
                    TransactionDate = p.PaymentDate
                })
                .ToList();

            var viewModel = new SuperAdminPaymentViewModel
            {
                TotalRevenue = totalRevenue,
                TotalTransactions = totalTransactions,
                SuccessfulPayments = successfulPayments,
                FailedPayments = failedPayments,
                RefundedPayments = refundedPayments,
                MonthlyRevenue = monthlyRevenue,
                MonthlyRevenueData = monthlyRevenueData,
                Payments = payments,
                SearchString = searchString,
                StatusFilter = statusFilter,
                MethodFilter = methodFilter,
                DateRangeFilter = dateRangeFilter,
                CurrentPage = page,
                TotalPages = totalPages
            };

            // Pass data for manual payment modal
            ViewBag.PaymentTenants = await _context.Tenants.OrderBy(t => t.CompanyName).ToListAsync();

            return View(viewModel);
        }

        // ═════════════════════════════════════════════
        // PAYMENT DETAILS — AJAX endpoint for modal
        // ═════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> PaymentDetails(int id)
        {
            var payment = await _context.Payments
                .Include(p => p.Tenant)
                .Include(p => p.Subscription)
                    .ThenInclude(s => s!.Plan)
                .FirstOrDefaultAsync(p => p.PaymentID == id);

            if (payment == null) return NotFound();

            var vm = new PaymentDetailsViewModel
            {
                PaymentID = payment.PaymentID,
                Amount = payment.Amount,
                Currency = payment.Currency,
                PaymentMethod = payment.PaymentMethod,
                PaymentStatus = payment.PaymentStatus,
                PaymentDate = payment.PaymentDate,
                CreatedAt = payment.CreatedAt,
                StripeSessionID = payment.StripeSessionID,
                StripePaymentIntentID = payment.StripePaymentIntentID,
                TenantID = payment.TenantID,
                CompanyName = payment.Tenant?.CompanyName ?? "Unknown",
                StripeCustomerId = payment.Tenant?.StripeCustomerId,
                SubscriptionID = payment.SubscriptionID,
                SubscriptionStatus = payment.Subscription?.Status ?? "N/A",
                SubscriptionStart = payment.Subscription?.StartDate,
                SubscriptionEnd = payment.Subscription?.EndDate,
                PlanName = payment.Subscription?.Plan?.PlanName ?? "N/A",
                PlanPrice = payment.Subscription?.Plan?.Price ?? 0
            };

            return Json(vm);
        }

        // ═════════════════════════════════════════════
        // CREATE MANUAL PAYMENT
        // ═════════════════════════════════════════════
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateManualPayment(int tenantId, decimal amount, string notes = "")
        {
            var tenant = await _context.Tenants.FindAsync(tenantId);
            if (tenant == null)
            {
                TempData["ErrorMessage"] = "Tenant not found.";
                return RedirectToAction(nameof(Payments));
            }

            // Find the tenant's most recent active subscription
            var subscription = await _context.Subscriptions
                .Where(s => s.TenantID == tenantId)
                .OrderByDescending(s => s.CreatedAt)
                .FirstOrDefaultAsync();

            if (subscription == null)
            {
                TempData["ErrorMessage"] = $"Tenant \"{tenant.CompanyName}\" has no subscription record. Create a subscription first.";
                return RedirectToAction(nameof(Payments));
            }

            var payment = new Payment
            {
                TenantID = tenantId,
                SubscriptionID = subscription.SubscriptionID,
                Amount = amount,
                Currency = "USD",
                PaymentMethod = "Manual",
                PaymentStatus = "Paid",
                StripeSessionID = $"MANUAL_{DateTime.Now:yyyyMMddHHmmss}",
                PaymentDate = DateTime.Now,
                CreatedAt = DateTime.Now
            };

            _context.Payments.Add(payment);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Manual payment of ${amount:F2} recorded for {tenant.CompanyName}.";
            return RedirectToAction(nameof(Payments));
        }

        // ═════════════════════════════════════════════
        // REVENUE SUMMARY — JSON for dashboard charts
        // ═════════════════════════════════════════════
        [HttpGet]
        public async Task<IActionResult> GetRevenueSummary()
        {
            var allPaid = await _context.Payments
                .Where(p => p.PaymentStatus == "Paid")
                .ToListAsync();

            var now = DateTime.Now;
            var monthlyData = new List<object>();

            for (int i = 11; i >= 0; i--)
            {
                var targetDate = now.AddMonths(-i);
                var monthPaid = allPaid
                    .Where(p => p.PaymentDate.Year == targetDate.Year && p.PaymentDate.Month == targetDate.Month);

                monthlyData.Add(new
                {
                    month = targetDate.ToString("MMM yyyy"),
                    revenue = monthPaid.Sum(p => p.Amount),
                    transactions = monthPaid.Count()
                });
            }

            var summary = new
            {
                totalRevenue = allPaid.Sum(p => p.Amount),
                thisMonthRevenue = allPaid.Where(p => p.PaymentDate.Year == now.Year && p.PaymentDate.Month == now.Month).Sum(p => p.Amount),
                lastMonthRevenue = allPaid.Where(p => p.PaymentDate.Year == now.AddMonths(-1).Year && p.PaymentDate.Month == now.AddMonths(-1).Month).Sum(p => p.Amount),
                monthlyBreakdown = monthlyData
            };

            return Json(summary);
        }

        // ═════════════════════════════════════════════
        // AUDIT LOGS — SPLIT INTO SYSTEM & SECURITY
        // ═════════════════════════════════════════════

        public async Task<IActionResult> SystemLogs(
            string? severity, string? search,
            DateTime? startDate, DateTime? endDate, int page = 1)
        {
            return await BuildAuditLogView("System", severity, search, startDate, endDate, page);
        }

        public async Task<IActionResult> SecurityLogs(
            string? severity, string? search,
            DateTime? startDate, DateTime? endDate, int page = 1)
        {
            return await BuildAuditLogView("Security", severity, search, startDate, endDate, page);
        }

        private async Task<IActionResult> BuildAuditLogView(
            string logType, string? severity, string? search,
            DateTime? startDate, DateTime? endDate, int page)
        {
            const int pageSize = 20;

            var allLogs = await _context.AuditLogs
                .Include(a => a.User)
                .Include(a => a.Tenant)
                .Where(a => a.LogType == logType)
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            // KPI counts
            int totalAll = allLogs.Count;
            int infoCount = allLogs.Count(a => a.Severity == "Info");
            int warningCount = allLogs.Count(a => a.Severity == "Warning");
            int criticalCount = allLogs.Count(a => a.Severity == "Critical");

            // Apply filters
            var query = allLogs.AsEnumerable();

            if (!string.IsNullOrEmpty(severity))
                query = query.Where(a => a.Severity == severity);

            if (startDate.HasValue)
                query = query.Where(a => a.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(a => a.CreatedAt <= endDate.Value.AddDays(1));

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.ToLower();
                query = query.Where(a =>
                    (a.Action?.ToLower().Contains(s) == true) ||
                    (a.Details?.ToLower().Contains(s) == true) ||
                    (a.User?.FullName?.ToLower().Contains(s) == true) ||
                    (a.Tenant?.CompanyName?.ToLower().Contains(s) == true)
                );
            }

            var filtered = query.ToList();
            int totalFiltered = filtered.Count;
            int totalPages = (int)Math.Ceiling(totalFiltered / (double)pageSize);
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var pagedLogs = filtered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AuditLogItem
                {
                    AuditLogID = a.AuditLogID,
                    LogType = a.LogType,
                    Severity = a.Severity,
                    Action = a.Action,
                    Details = a.Details,
                    IPAddress = a.IPAddress,
                    CreatedAt = a.CreatedAt,
                    UserName = a.User?.FullName ?? "System",
                    CompanyName = a.Tenant?.CompanyName ?? "—"
                }).ToList();

            var vm = new AuditLogViewModel
            {
                TotalLogs = totalAll,
                SystemLogs = infoCount,
                SecurityLogs = warningCount,
                CriticalLogs = criticalCount,
                Logs = pagedLogs,
                LogTypeFilter = logType,
                SeverityFilter = severity,
                SearchQuery = search,
                StartDate = startDate,
                EndDate = endDate,
                CurrentPage = page,
                TotalPages = totalPages,
                PageSize = pageSize
            };

            return View(logType == "System" ? "SystemLogs" : "SecurityLogs", vm);
        }

        [HttpGet]
        public async Task<IActionResult> GetLogDetails(int id)
        {
            var log = await _context.AuditLogs
                .Include(a => a.User)
                .Include(a => a.Tenant)
                .FirstOrDefaultAsync(a => a.AuditLogID == id);

            if (log == null)
                return Json(new { success = false });

            return Json(new
            {
                success = true,
                data = new AuditLogDetailItem
                {
                    AuditLogID = log.AuditLogID,
                    LogType = log.LogType,
                    Severity = log.Severity,
                    Action = log.Action,
                    Details = log.Details,
                    IPAddress = log.IPAddress,
                    CreatedAt = log.CreatedAt,
                    UserName = log.User?.FullName ?? "System",
                    UserEmail = log.User?.Email ?? "—",
                    CompanyName = log.Tenant?.CompanyName ?? "—"
                }
            });
        }
    }
}
