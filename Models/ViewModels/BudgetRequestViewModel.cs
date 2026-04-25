using System;

namespace FinSight.Models.ViewModels
{
    /// <summary>
    /// Flat ViewModel for presenting budget request data
    /// in the Approval Workflow view, including submitter/approver names.
    /// </summary>
    public class BudgetRequestViewModel
    {
        public int RequestID { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public int DepartmentID { get; set; }
        public decimal RequestedAmount { get; set; }
        public string BudgetCategory { get; set; } = string.Empty;
        public int BudgetID { get; set; }
        public string Status { get; set; } = string.Empty;

        // Submitter info
        public string SubmittedByName { get; set; } = string.Empty;
        public DateTime SubmittedDate { get; set; }

        // Approver info
        public string? ApprovedByName { get; set; }
        public DateTime? ApprovedDate { get; set; }

        // Rejection
        public string? RejectionReason { get; set; }

        // Audit
        public string? UpdatedByName { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
