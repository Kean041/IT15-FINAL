using System;
using System.Collections.Generic;

namespace FinSight.Models.ViewModels
{
    public class SuperAdminDashboardViewModel
    {
        // ── KPI Summary Cards ──
        public int TotalTenants { get; set; }
        public int ActiveTenants { get; set; }
        public int PendingTenants { get; set; }
        public int SuspendedTenants { get; set; }
        public int TotalSubscriptions { get; set; }
        public int ActiveSubscriptions { get; set; }
        public int ExpiredSubscriptions { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal MonthlyRevenue { get; set; }
        public int TotalPayments { get; set; }
        public int FailedPayments { get; set; }
        public int NewSignupsThisMonth { get; set; }

        // ── Revenue Analytics (Chart.js) ──
        public List<MonthlyRevenuePoint> RevenueChart { get; set; } = new();

        // ── Subscription Analytics ──
        public List<PlanDistribution> SubscriptionsByPlan { get; set; } = new();
        public string MostPopularPlan { get; set; } = "N/A";

        // ── Payment Analytics ──
        public int StripePayments { get; set; }
        public int ManualPayments { get; set; }

        // ── Recent Activity ──
        public List<RecentTenantItem> RecentTenants { get; set; } = new();
        public List<RecentSubscriptionItem> RecentSubscriptions { get; set; } = new();
        public List<RecentPaymentItem> RecentPayments { get; set; } = new();
    }

    public class MonthlyRevenuePoint
    {
        public string Month { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
    }

    public class PlanDistribution
    {
        public string PlanName { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class RecentTenantItem
    {
        public int TenantID { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Plan { get; set; } = string.Empty;
        public DateTime? CreatedDate { get; set; }
    }

    public class RecentSubscriptionItem
    {
        public int SubscriptionID { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
    }

    public class RecentPaymentItem
    {
        public int PaymentID { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public string Method { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime PaymentDate { get; set; }
    }
}
