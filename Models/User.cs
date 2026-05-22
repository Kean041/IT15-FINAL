using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("Users")]
    public class User
    {
        [Key]
        public int UserID { get; set; }

        [Required]
        [StringLength(500)]
        public string FullName { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(500)]
        public string PasswordHash { get; set; } = string.Empty;

        public int? RoleID { get; set; } = 1; // Default: Admin

        public int? DepartmentID { get; set; }

        public int? TenantID { get; set; }

        public bool IsArchived { get; set; } = false;

        [Column("CreatedAt")]
        public DateTime? CreatedAt { get; set; } = DateTime.Now;

        [Column("UpdatedAt")]
        public DateTime? UpdatedAt { get; set; }

        // Account lockout support
        public int FailedLoginAttempts { get; set; } = 0;
        public DateTime? LockoutEnd { get; set; }

        // ── Two-Factor Authentication ──
        public bool IsTwoFactorEnabled { get; set; } = false;

        [StringLength(500)]
        public string? TwoFactorSecretKey { get; set; }  // Reserved for future authenticator app support

        [StringLength(500)]
        public string? OTPCode { get; set; }              // Hashed OTP (never stored in plaintext)

        public DateTime? OTPExpiration { get; set; }
        public int FailedOTPAttempts { get; set; } = 0;
        public DateTime? OTPLockoutEnd { get; set; }
        public DateTime? LastOTPSentAt { get; set; }

        // Navigation properties
        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }

        [ForeignKey("DepartmentID")]
        public Department? Department { get; set; }
    }
}
