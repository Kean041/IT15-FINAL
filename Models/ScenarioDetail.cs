using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("ScenarioDetails")]
    public class ScenarioDetail
    {
        [Key]
        public int ScenarioDetailID { get; set; }

        [Required]
        public int ScenarioID { get; set; }

        [Required]
        public int BudgetID { get; set; }

        [Required]
        public int DepartmentID { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal AdjustedAmount { get; set; }

        [Required]
        public int TenantID { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        [ForeignKey("ScenarioID")]
        public Scenario? Scenario { get; set; }

        [ForeignKey("BudgetID")]
        public Budget? Budget { get; set; }

        [ForeignKey("DepartmentID")]
        public Department? Department { get; set; }

        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }
    }
}
