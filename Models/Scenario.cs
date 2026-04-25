using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("Scenarios")]
    public class Scenario
    {
        [Key]
        public int ScenarioID { get; set; }

        [Required]
        [StringLength(255)]
        public string ScenarioName { get; set; } = string.Empty;

        [StringLength(1000)]
        public string? Description { get; set; }

        [Required]
        public int TenantID { get; set; }

        [Required]
        public int CreatedBy { get; set; }

        [Column("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }

        [ForeignKey("CreatedBy")]
        public User? Creator { get; set; }

        public ICollection<ScenarioDetail> ScenarioDetails { get; set; } = new List<ScenarioDetail>();
    }
}
