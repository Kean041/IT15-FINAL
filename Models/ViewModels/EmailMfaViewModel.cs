using System.ComponentModel.DataAnnotations;

namespace FinSight.Models.ViewModels
{
    public class EmailMfaViewModel
    {
        [Required(ErrorMessage = "Please enter the verification code.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits.")]
        [RegularExpression("^[0-9]*$", ErrorMessage = "Code must be numeric.")]
        public string Code { get; set; } = string.Empty;
        
        public string? Email { get; set; }
        
        public int RemainingAttempts { get; set; } = 5;
    }
}
