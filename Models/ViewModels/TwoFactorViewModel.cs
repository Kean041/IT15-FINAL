using System.ComponentModel.DataAnnotations;

namespace FinSight.Models.ViewModels
{
    public class TwoFactorViewModel
    {
        [Required(ErrorMessage = "Authentication code is required.")]
        [StringLength(7, ErrorMessage = "The {0} must be at least {2} and at max {1} characters long.", MinimumLength = 6)]
        [DataType(DataType.Text)]
        [Display(Name = "Authentication Code")]
        public string Code { get; set; } = string.Empty;

        public string? SecretKey { get; set; }
        public string? QrCodeUri { get; set; }
    }
}
