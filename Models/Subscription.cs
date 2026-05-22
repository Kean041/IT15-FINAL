using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("Subscriptions")]
    public class Subscription
    {
        [Key]
        public int SubscriptionID { get; set; }

        [Required]
        public int TenantID { get; set; }

        [Required]
        public int PlanID { get; set; }

        [StringLength(150)]
        public string? StripeSubscriptionID { get; set; }

        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        [Required]
        [StringLength(50)]
        public string Status { get; set; } = "Pending"; // Active, Expired, Cancelled, Pending

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }

        [ForeignKey("PlanID")]
        public Plan? Plan { get; set; }

        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
