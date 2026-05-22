using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("Payments")]
    public class Payment
    {
        [Key]
        public int PaymentID { get; set; }

        [Required]
        public int TenantID { get; set; }

        [Required]
        public int SubscriptionID { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(10)]
        public string Currency { get; set; } = "USD";

        [Required]
        [StringLength(50)]
        public string PaymentMethod { get; set; } = "Stripe";

        [Required]
        [StringLength(50)]
        public string PaymentStatus { get; set; } = "Pending"; // Paid, Pending, Failed, Refunded

        [StringLength(150)]
        public string? StripeSessionID { get; set; }

        [StringLength(150)]
        public string? StripePaymentIntentID { get; set; }

        [Required]
        public DateTime PaymentDate { get; set; } = DateTime.Now;

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation properties
        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }

        [ForeignKey("SubscriptionID")]
        public Subscription? Subscription { get; set; }
    }
}
