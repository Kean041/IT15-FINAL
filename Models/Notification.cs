using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("Notifications")]
    public class Notification
    {
        [Key]
        public int NotificationID { get; set; }

        public int? TenantID { get; set; }

        public int? UserID { get; set; }

        [Required]
        [StringLength(150)]
        public string Title { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string Message { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string NotificationType { get; set; } = "System"; // System, Approval, Payment, Subscription, Security

        public bool IsRead { get; set; } = false;

        [StringLength(255)]
        public string? RedirectUrl { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation properties
        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }

        [ForeignKey("UserID")]
        public User? User { get; set; }
    }
}
