using System;
using System.Collections.Generic;

namespace FinSight.Models.ViewModels
{
    public class SuperAdminPaymentViewModel
    {
        // KPIs
        public decimal TotalRevenue { get; set; }
        public int TotalTransactions { get; set; }
        public int SuccessfulPayments { get; set; }
        public int FailedPayments { get; set; }
        public int RefundedPayments { get; set; }
        public decimal MonthlyRevenue { get; set; }

        // Data list
        public List<PaymentRecord> Payments { get; set; } = new();

        // Monthly revenue chart data
        public List<MonthlyRevenueSummary> MonthlyRevenueData { get; set; } = new();

        // Filters state
        public string? SearchString { get; set; }
        public string? StatusFilter { get; set; }
        public string? MethodFilter { get; set; }
        public string? DateRangeFilter { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
    }

    public class PaymentRecord
    {
        public int PaymentID { get; set; }
        public int TenantID { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public decimal AmountPaid { get; set; }
        public string Currency { get; set; } = "USD";
        public string PaymentMethod { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // Paid, Pending, Failed, Refunded
        public string ReferenceID { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
    }

    public class PaymentDetailsViewModel
    {
        // Payment info
        public int PaymentID { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; } = "USD";
        public string PaymentMethod { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
        public DateTime PaymentDate { get; set; }
        public DateTime CreatedAt { get; set; }

        // Stripe identifiers
        public string? StripeSessionID { get; set; }
        public string? StripePaymentIntentID { get; set; }

        // Tenant info
        public int TenantID { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string? StripeCustomerId { get; set; }

        // Subscription info
        public int SubscriptionID { get; set; }
        public string SubscriptionStatus { get; set; } = string.Empty;
        public DateTime? SubscriptionStart { get; set; }
        public DateTime? SubscriptionEnd { get; set; }

        // Plan info
        public string PlanName { get; set; } = string.Empty;
        public decimal PlanPrice { get; set; }
    }

    public class MonthlyRevenueSummary
    {
        public string Month { get; set; } = string.Empty; // "Jan 2026"
        public decimal Revenue { get; set; }
        public int TransactionCount { get; set; }
    }
}
