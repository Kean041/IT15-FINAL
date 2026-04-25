using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("Forecasts")]
    public class Forecast
    {
        [Key]
        public int ForecastID { get; set; }

        [Required]
        public int DepartmentID { get; set; }

        [Required]
        public int TenantID { get; set; }

        [Required]
        public int BudgetID { get; set; }

        [Required]
        [StringLength(50)]
        public string ForecastType { get; set; } = "Base Case";

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PredictedAmount { get; set; }

        [Required]
        public int Year { get; set; }

        [Required]
        public int CreatedBy { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        [ForeignKey("DepartmentID")]
        public Department? Department { get; set; }

        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }

        [ForeignKey("BudgetID")]
        public Budget? Budget { get; set; }

        [ForeignKey("CreatedBy")]
        public User? Creator { get; set; }
    }
}
