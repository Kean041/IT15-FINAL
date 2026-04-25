using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("BudgetRequests")]
    public class BudgetRequest
    {
        [Key]
        public int RequestID { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal RequestedAmount { get; set; }

        [Required]
        public int DepartmentID { get; set; }

        [Required]
        public int TenantID { get; set; }

        [Required]
        public int BudgetID { get; set; }

        [Required]
        public int SubmittedBy { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Pending";

        public int? ApprovedBy { get; set; }

        public DateTime? ApprovedDate { get; set; }

        [StringLength(1000)]
        public string? RejectionReason { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public int? UpdatedBy { get; set; }

        [Column("UpdatedAt")]
        public DateTime? UpdatedAt { get; set; }

        // Navigation Properties
        [ForeignKey("DepartmentID")]
        public Department? Department { get; set; }

        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }

        [ForeignKey("BudgetID")]
        public Budget? Budget { get; set; }

        [ForeignKey("SubmittedBy")]
        public User? Submitter { get; set; }

        [ForeignKey("ApprovedBy")]
        public User? Approver { get; set; }
    }
}
