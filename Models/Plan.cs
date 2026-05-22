using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("Plans")]
    public class Plan
    {
        [Key]
        public int PlanID { get; set; }

        [Required]
        [StringLength(100)]
        public string PlanName { get; set; } = string.Empty;

        [StringLength(255)]
        public string? Description { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        [Required]
        public int DurationInMonths { get; set; } = 1;

        [StringLength(150)]
        public string? StripePriceID { get; set; }

        [Required]
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    }
}
