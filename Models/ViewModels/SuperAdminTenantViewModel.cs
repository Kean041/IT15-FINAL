using System;
using System.Collections.Generic;

namespace FinSight.Models.ViewModels
{
    public class SuperAdminTenantViewModel
    {
        // KPIs
        public int TotalTenants { get; set; }
        public int ActiveTenants { get; set; }
        public int SuspendedTenants { get; set; }
        public int PendingTenants { get; set; }
        public int NewTenantsThisMonth { get; set; }

        // Data list
        public List<TenantRecord> Tenants { get; set; } = new();

        // Filters state
        public string? SearchString { get; set; }
        public string? StatusFilter { get; set; }
        public string? PlanFilter { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
    }

    public class TenantRecord
    {
        public int TenantID { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string AdminEmail { get; set; } = string.Empty;
        public string SubscriptionPlan { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Active", "Pending", "Suspended", "Inactive"
        public DateTime CreatedDate { get; set; }
        public int TotalUsers { get; set; }
    }
}
