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

        // Data list
        public List<PaymentRecord> Payments { get; set; } = new();

        // Filters state
        public string? SearchString { get; set; }
        public string? StatusFilter { get; set; }
        public string? DateRangeFilter { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; } = 1;
    }

    public class PaymentRecord
    {
        public int PaymentID { get; set; }
        public string TenantName { get; set; } = string.Empty;
        public string PlanName { get; set; } = string.Empty;
        public decimal AmountPaid { get; set; }
        public string PaymentMethod { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Paid", "Pending", "Failed"
        public string ReferenceID { get; set; } = string.Empty;
        public DateTime TransactionDate { get; set; }
    }
}
