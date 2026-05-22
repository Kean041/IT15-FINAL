using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace FinSight.Models.ViewModels
{
    public class VerifyOtpViewModel
    {
        [Required(ErrorMessage = "Please enter the verification code.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "Code must be 6 digits.")]
        [Display(Name = "Verification Code")]
        public string OTPCode { get; set; } = string.Empty;

        /// <summary>Masked email for display (e.g., "j***@company.com")</summary>
        public string? MaskedEmail { get; set; }

        /// <summary>Seconds remaining until OTP expires</summary>
        [JsonRequired]
        public int RemainingSeconds { get; set; }

        /// <summary>Whether the resend button should be enabled</summary>
        [JsonRequired]
        public bool CanResend { get; set; }

        /// <summary>Seconds until resend is available again</summary>
        [JsonRequired]
        public int ResendCooldownSeconds { get; set; }
    }
}
