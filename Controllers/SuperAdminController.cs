using FinSight.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace FinSight.Controllers
{
    public class SuperAdminController : BaseController
    {
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

        public IActionResult Dashboard()
        {
            return View();
        }

        public IActionResult Tenants(string searchString = "", string statusFilter = "", string planFilter = "", int page = 1)
        {
            // Mock data generation
            var allTenants = new List<FinSight.Models.ViewModels.TenantRecord>
            {
                new FinSight.Models.ViewModels.TenantRecord { TenantID = 1, CompanyName = "Acme Corp", AdminEmail = "admin@acmecorp.com", SubscriptionPlan = "Enterprise", Status = "Active", CreatedDate = DateTime.Now.AddMonths(-6) },
                new FinSight.Models.ViewModels.TenantRecord { TenantID = 2, CompanyName = "TechNova", AdminEmail = "it@technova.io", SubscriptionPlan = "Professional", Status = "Active", CreatedDate = DateTime.Now.AddMonths(-11) },
                new FinSight.Models.ViewModels.TenantRecord { TenantID = 3, CompanyName = "Global Industries", AdminEmail = "finance@globalind.com", SubscriptionPlan = "Enterprise", Status = "Suspended", CreatedDate = DateTime.Now.AddMonths(-12) },
                new FinSight.Models.ViewModels.TenantRecord { TenantID = 4, CompanyName = "NextGen LLC", AdminEmail = "ceo@nextgenllc.co", SubscriptionPlan = "Starter", Status = "Active", CreatedDate = DateTime.Now.AddDays(-15) },
                new FinSight.Models.ViewModels.TenantRecord { TenantID = 5, CompanyName = "DataSync", AdminEmail = "sysadmin@datasync.net", SubscriptionPlan = "Professional", Status = "Inactive", CreatedDate = DateTime.Now.AddDays(-2) },
                new FinSight.Models.ViewModels.TenantRecord { TenantID = 6, CompanyName = "CloudFirst", AdminEmail = "admin@cloudfirst.cloud", SubscriptionPlan = "Enterprise", Status = "Active", CreatedDate = DateTime.Now.AddMonths(-5) }
            };

            // Applying Filters
            if (!string.IsNullOrEmpty(searchString))
            {
                allTenants = allTenants.Where(t => t.CompanyName.Contains(searchString, StringComparison.OrdinalIgnoreCase) || 
                                                   t.AdminEmail.Contains(searchString, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                allTenants = allTenants.Where(t => t.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(planFilter))
            {
                allTenants = allTenants.Where(t => t.SubscriptionPlan.Equals(planFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            
            // Pagination mock logic
            int pageSize = 10;
            var pagedTenants = allTenants.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var viewModel = new FinSight.Models.ViewModels.SuperAdminTenantViewModel
            {
                TotalTenants = 6,
                ActiveTenants = 4,
                SuspendedTenants = 1,
                NewTenantsThisMonth = 2,
                Tenants = pagedTenants,
                SearchString = searchString,
                StatusFilter = statusFilter,
                PlanFilter = planFilter,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(allTenants.Count / (double)pageSize) == 0 ? 1 : (int)Math.Ceiling(allTenants.Count / (double)pageSize)
            };

            return View(viewModel);
        }

        public IActionResult Subscriptions(string searchString = "", string statusFilter = "", string planFilter = "", string dateRangeFilter = "", int page = 1)
        {
            // Mock data generation
            var allSubscriptions = new List<FinSight.Models.ViewModels.SubscriptionRecord>
            {
                new FinSight.Models.ViewModels.SubscriptionRecord { SubscriptionID = 2001, TenantName = "Acme Corp", PlanName = "Enterprise", StartDate = DateTime.Now.AddMonths(-6), EndDate = DateTime.Now.AddMonths(6), Status = "Active", PaymentStatus = "Paid" },
                new FinSight.Models.ViewModels.SubscriptionRecord { SubscriptionID = 2002, TenantName = "TechNova", PlanName = "Professional", StartDate = DateTime.Now.AddMonths(-11), EndDate = DateTime.Now.AddDays(15), Status = "Active", PaymentStatus = "Paid" },
                new FinSight.Models.ViewModels.SubscriptionRecord { SubscriptionID = 2003, TenantName = "Global Industries", PlanName = "Enterprise", StartDate = DateTime.Now.AddMonths(-12), EndDate = DateTime.Now.AddDays(-2), Status = "Expired", PaymentStatus = "Failed" },
                new FinSight.Models.ViewModels.SubscriptionRecord { SubscriptionID = 2004, TenantName = "NextGen LLC", PlanName = "Starter", StartDate = DateTime.Now.AddMonths(-1), EndDate = DateTime.Now.AddMonths(11), Status = "Active", PaymentStatus = "Paid" },
                new FinSight.Models.ViewModels.SubscriptionRecord { SubscriptionID = 2005, TenantName = "DataSync", PlanName = "Professional", StartDate = DateTime.Now.AddDays(-2), EndDate = DateTime.Now.AddMonths(1), Status = "Pending", PaymentStatus = "Pending" },
                new FinSight.Models.ViewModels.SubscriptionRecord { SubscriptionID = 2006, TenantName = "CloudFirst", PlanName = "Enterprise", StartDate = DateTime.Now.AddMonths(-5), EndDate = DateTime.Now.AddMonths(7), Status = "Active", PaymentStatus = "Paid" }
            };

            // Applying Filters
            if (!string.IsNullOrEmpty(searchString))
            {
                allSubscriptions = allSubscriptions.Where(s => s.TenantName.Contains(searchString, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                allSubscriptions = allSubscriptions.Where(s => s.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(planFilter))
            {
                allSubscriptions = allSubscriptions.Where(s => s.PlanName.Equals(planFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            
            // Pagination mock logic
            int pageSize = 10;
            var pagedSubscriptions = allSubscriptions.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var viewModel = new FinSight.Models.ViewModels.SuperAdminSubscriptionViewModel
            {
                TotalActiveSubscriptions = 4,
                ExpiringSoon = 1,
                ExpiredSubscriptions = 1,
                TotalTenants = 6,
                Subscriptions = pagedSubscriptions,
                SearchString = searchString,
                StatusFilter = statusFilter,
                PlanFilter = planFilter,
                DateRangeFilter = dateRangeFilter,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(allSubscriptions.Count / (double)pageSize) == 0 ? 1 : (int)Math.Ceiling(allSubscriptions.Count / (double)pageSize)
            };

            return View(viewModel);
        }

        public IActionResult Payments(string searchString = "", string statusFilter = "", string dateRangeFilter = "", int page = 1)
        {
            // Mock data generation
            var allPayments = new List<FinSight.Models.ViewModels.PaymentRecord>
            {
                new FinSight.Models.ViewModels.PaymentRecord { PaymentID = 1001, TenantName = "Acme Corp", PlanName = "Enterprise", AmountPaid = 499.00m, PaymentMethod = "PayMongo", Status = "Paid", ReferenceID = "PM_9X2B44", TransactionDate = DateTime.Now.AddDays(-1) },
                new FinSight.Models.ViewModels.PaymentRecord { PaymentID = 1002, TenantName = "TechNova", PlanName = "Professional", AmountPaid = 199.00m, PaymentMethod = "Stripe", Status = "Paid", ReferenceID = "ST_8X1A22", TransactionDate = DateTime.Now.AddDays(-2) },
                new FinSight.Models.ViewModels.PaymentRecord { PaymentID = 1003, TenantName = "Global Industries", PlanName = "Enterprise", AmountPaid = 499.00m, PaymentMethod = "PayMongo", Status = "Failed", ReferenceID = "PM_7X0C11", TransactionDate = DateTime.Now.AddDays(-3) },
                new FinSight.Models.ViewModels.PaymentRecord { PaymentID = 1004, TenantName = "NextGen LLC", PlanName = "Starter", AmountPaid = 49.00m, PaymentMethod = "PayPal", Status = "Paid", ReferenceID = "PP_6Y9D00", TransactionDate = DateTime.Now.AddDays(-5) },
                new FinSight.Models.ViewModels.PaymentRecord { PaymentID = 1005, TenantName = "DataSync", PlanName = "Professional", AmountPaid = 199.00m, PaymentMethod = "Stripe", Status = "Pending", ReferenceID = "ST_5Z8E99", TransactionDate = DateTime.Now.AddDays(-6) },
                new FinSight.Models.ViewModels.PaymentRecord { PaymentID = 1006, TenantName = "CloudFirst", PlanName = "Enterprise", AmountPaid = 499.00m, PaymentMethod = "PayMongo", Status = "Paid", ReferenceID = "PM_4A7F88", TransactionDate = DateTime.Now.AddDays(-10) }
            };

            // Applying Filters
            if (!string.IsNullOrEmpty(searchString))
            {
                allPayments = allPayments.Where(p => p.TenantName.Contains(searchString, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (!string.IsNullOrEmpty(statusFilter))
            {
                allPayments = allPayments.Where(p => p.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            
            // Pagination mock logic
            int pageSize = 10;
            var pagedPayments = allPayments.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var viewModel = new FinSight.Models.ViewModels.SuperAdminPaymentViewModel
            {
                TotalRevenue = 1745.00m,
                TotalTransactions = 6,
                SuccessfulPayments = 4,
                FailedPayments = 1,
                Payments = pagedPayments,
                SearchString = searchString,
                StatusFilter = statusFilter,
                DateRangeFilter = dateRangeFilter,
                CurrentPage = page,
                TotalPages = (int)Math.Ceiling(allPayments.Count / (double)pageSize) == 0 ? 1 : (int)Math.Ceiling(allPayments.Count / (double)pageSize)
            };

            return View(viewModel);
        }
    }
}
