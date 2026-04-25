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
    }
}
