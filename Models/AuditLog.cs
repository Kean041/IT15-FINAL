using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FinSight.Models
{
    [Table("AuditLogs")]
    public class AuditLog
    {
        [Key]
        public int AuditLogID { get; set; }

        public int? TenantID { get; set; }

        public int? UserID { get; set; }

        [Required]
        [StringLength(50)]
        public string LogType { get; set; } = "System"; // "System" or "Security"

        [Required]
        [StringLength(50)]
        public string Severity { get; set; } = "Info"; // "Info", "Warning", "Critical"

        [Required]
        [StringLength(200)]
        public string Action { get; set; } = string.Empty;

        [StringLength(2000)]
        public string? Details { get; set; }

        [StringLength(50)]
        public string? IPAddress { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation properties
        [ForeignKey("TenantID")]
        public Tenant? Tenant { get; set; }

        [ForeignKey("UserID")]
        public User? User { get; set; }
    }
}
