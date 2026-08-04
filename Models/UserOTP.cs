using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("UserOTPs")]
    public class UserOTP
    {
        [Key]
        public int OTPID { get; set; }

        [Required]
        public int UserID { get; set; }

        public int? TenantID { get; set; }

        [Required]
        [StringLength(255)]
        public string OTPHash { get; set; } = string.Empty;

        [Required]
        public DateTime GeneratedAt { get; set; }

        [Required]
        public DateTime ExpiresAt { get; set; }

        public DateTime? UsedAt { get; set; }

        [Required]
        public bool IsUsed { get; set; } = false;

        [Required]
        public int AttemptCount { get; set; } = 0;

        [Required]
        public bool IsExpired { get; set; } = false;

        [StringLength(50)]
        public string? CreatedByIP { get; set; }

        [ForeignKey("UserID")]
        public User? User { get; set; }

        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }
    }
}
