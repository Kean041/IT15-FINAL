using System;
using System.Collections.Generic;

namespace FinSight.Models.ViewModels
{
    /// <summary>
    /// ViewModel for the Tenant Details page/modal in the SuperAdmin module.
    /// </summary>
    public class TenantDetailsViewModel
    {
        // ── Tenant Info ──
        public int TenantID { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string SubscriptionPlan { get; set; } = string.Empty;
        public string SubscriptionStatus { get; set; } = string.Empty;
        public string? StripeCustomerId { get; set; }
        public string? StripeSubscriptionId { get; set; }
        public DateTime? CreatedDate { get; set; }

        // ── Admin User Info ──
        public string AdminName { get; set; } = "N/A";
        public string AdminEmail { get; set; } = "N/A";
        public DateTime? AdminCreatedAt { get; set; }

        // ── Counts ──
        public int TotalUsers { get; set; }
    }
}
