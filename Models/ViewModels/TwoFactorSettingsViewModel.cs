namespace FinSight.Models.ViewModels
{
    public class TwoFactorSettingsViewModel
    {
        /// <summary>Whether 2FA is currently enabled for this user</summary>
        public bool IsTwoFactorEnabled { get; set; }

        /// <summary>True if the user's role mandates 2FA (cannot be disabled)</summary>
        public bool IsMandatory { get; set; }

        /// <summary>User's role name for display</summary>
        public string RoleName { get; set; } = string.Empty;

        /// <summary>Success/error message after toggle</summary>
        public string? StatusMessage { get; set; }
    }
}
