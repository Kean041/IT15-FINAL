using System;
using System.Collections.Generic;

namespace FinSight.Models.ViewModels
{
    public class SuperAdminSubscriptionViewModel
    {
        // KPIs
        public int TotalActiveSubscriptions { get; set; }
        public int ExpiringSoon { get; set; }
        public int ExpiredSubscriptions { get; set; }
        public int TotalTenants { get; set; }

        // Data list
        public List<SubscriptionRecord> Subscriptions { get; set; } = new();

        // Filters state
        public string? SearchString { get; set; }
        public string? StatusFilter { get; set; }
        public string? PlanFilter { get; set; }
        public string? DateRangeFilter { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
    }

    public class SubscriptionRecord
    {
        public int SubscriptionID { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string Status { get; set; } = string.Empty; // "Active", "Expired", "Pending"
        public string PaymentStatus { get; set; } = string.Empty; // "Paid", "Pending", "Failed"
    }
}
