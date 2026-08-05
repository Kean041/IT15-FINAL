using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("Expenses")]
    public class Expense
    {
        [Key]
        public int ExpenseID { get; set; }

        public int? BudgetRequestID { get; set; }

        [Required]
        public int BudgetID { get; set; }

        [Required]
        public int DepartmentID { get; set; }

        [Required]
        public int TenantID { get; set; }

        [Required]
        [StringLength(255)]
        public string ExpenseTitle { get; set; } = string.Empty;

        [Required]
        [StringLength(255)]
        public string Category { get; set; } = string.Empty;

        [Required]
        [StringLength(1000)]
        public string Description { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        public DateTime ExpenseDate { get; set; } = DateTime.Now;

        [Required]
        [NotMapped]
        public int Year { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Recorded"; // Recorded, Verified, Archived

        [Required]
        [NotMapped]
        public int CreatedBy { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        [ForeignKey("BudgetRequestID")]
        public BudgetRequest? BudgetRequest { get; set; }

        [ForeignKey("BudgetID")]
        public Budget? Budget { get; set; }

        [ForeignKey("DepartmentID")]
        public Department? Department { get; set; }

        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }

        [NotMapped]
        public User? Creator { get; set; }
    }
}
