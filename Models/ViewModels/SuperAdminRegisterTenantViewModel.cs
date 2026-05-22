using System.ComponentModel.DataAnnotations;

namespace FinSight.Models.ViewModels
{
    public class SuperAdminRegisterTenantViewModel
    {
        [Required(ErrorMessage = "Company Name is required")]
        [StringLength(200)]
        public string CompanyName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Admin Full Name is required")]
        [StringLength(200)]
        public string AdminName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Admin Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        public string AdminEmail { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password is required")]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
        public string AdminPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Subscription Plan is required")]
        public string SubscriptionPlan { get; set; } = "Basic";
        
        public string SubscriptionStatus { get; set; } = "Active";
    }
}
