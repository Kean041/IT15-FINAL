using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("Budgets")]
    public class Budget
    {
        [Key]
        public int BudgetID { get; set; }

        [Required]
        public int DepartmentID { get; set; }

        [Required]
        public int TenantID { get; set; }

        [Required]
        [StringLength(255)]
        public string Category { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        public int Year { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Draft";

        [Required]
        public int CreatedBy { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Column("UpdatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        [ForeignKey("DepartmentID")]
        public Department? Department { get; set; }

        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }

        [ForeignKey("CreatedBy")]
        public User? Creator { get; set; }
    }
}
