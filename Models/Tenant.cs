using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("Tenants")]
    public class Tenant
    {
        [Key]
        public int TenantID { get; set; }

        [StringLength(500)]
        public string? CompanyName { get; set; }

        public DateTime? CreatedDate { get; set; } = DateTime.Now;

        [StringLength(50)]
        public string? SubscriptionPlan { get; set; }

        [StringLength(100)]
        public string? StripeCustomerId { get; set; }

        [StringLength(100)]
        public string? StripeSubscriptionId { get; set; }

        [StringLength(50)]
        public string? SubscriptionStatus { get; set; } = "Pending"; // "Active", "PastDue", "Canceled"

        // Navigation property — all users belonging to this tenant
        public ICollection<User> Users { get; set; } = new List<User>();

        // Navigation property — subscriptions for this tenant
        public ICollection<Subscription> Subscriptions { get; set; } = new List<Subscription>();

        // Navigation property — payments for this tenant
        public ICollection<Payment> Payments { get; set; } = new List<Payment>();
    }
}
