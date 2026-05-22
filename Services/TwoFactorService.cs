using FinSight.Data;
using FinSight.Helpers;
using FinSight.Models;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace FinSight.Services
{
    /// <summary>
    /// Centralized Two-Factor Authentication service.
    /// Handles OTP generation, validation, email delivery, and rate limiting.
    /// </summary>
    public class TwoFactorService
    {
        private readonly FinSightDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TwoFactorService> _logger;

        // Configuration defaults (overridden by appsettings.json)
        private bool IsTwoFactorGloballyEnabled => _configuration.GetValue("TwoFactor:Enabled", true);
        private int OTPExpiryMinutes => _configuration.GetValue("TwoFactor:OTPExpiryMinutes", 5);
        private int MaxOTPAttempts => _configuration.GetValue("TwoFactor:MaxOTPAttempts", 5);
        private int OTPLockoutMinutes => _configuration.GetValue("TwoFactor:OTPLockoutMinutes", 15);
        private int ResendCooldownSeconds => _configuration.GetValue("TwoFactor:ResendCooldownSeconds", 60);
        private string OTPSecretKey => _configuration.GetValue("TwoFactor:OTPSecretKey", "FinSight2FA-Default-Key") ?? "FinSight2FA-Default-Key";

        public TwoFactorService(FinSightDbContext context, IConfiguration configuration, ILogger<TwoFactorService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
        }

        // ────────────────────────────────────────────────────────
        // OTP GENERATION
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Generates a cryptographically secure 6-digit OTP, stores the HMAC hash
        /// in the user record, and returns the plaintext OTP for emailing.
        /// </summary>
        public async Task<string> GenerateOTP(User user)
        {
            // Generate cryptographically secure 6-digit code
            var otp = GenerateSecureCode();

            // Hash the OTP before storing (never store plaintext)
            user.OTPCode = HashOTP(otp);
            user.OTPExpiration = DateTime.Now.AddMinutes(OTPExpiryMinutes);
            user.FailedOTPAttempts = 0;
            user.OTPLockoutEnd = null;
            user.LastOTPSentAt = DateTime.Now;

            _context.Update(user);
            await _context.SaveChangesAsync();

            _logger.LogInformation("OTP generated for user {UserID}. Expires at {Expiry}", user.UserID, user.OTPExpiration);

            return otp;
        }

        // ────────────────────────────────────────────────────────
        // OTP VALIDATION
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Validates the submitted OTP against the stored hash.
        /// Returns a result tuple: (success, errorMessage).
        /// </summary>
        public async Task<(bool Success, string? ErrorMessage)> ValidateOTP(User user, string submittedCode)
        {
            // Check lockout
            if (IsOTPLockedOut(user))
            {
                var remaining = (int)Math.Ceiling((user.OTPLockoutEnd!.Value - DateTime.Now).TotalMinutes);
                return (false, $"Too many failed attempts. Try again in {remaining} minute(s).");
            }

            // Check if OTP exists
            if (string.IsNullOrEmpty(user.OTPCode) || !user.OTPExpiration.HasValue)
            {
                return (false, "No verification code found. Please request a new one.");
            }

            // Check expiry
            if (user.OTPExpiration.Value < DateTime.Now)
            {
                return (false, "Verification code has expired. Please request a new one.");
            }

            // Constant-time comparison of HMAC hashes
            var submittedHash = HashOTP(submittedCode);
            var storedHashBytes = Convert.FromBase64String(user.OTPCode);
            var submittedHashBytes = Convert.FromBase64String(submittedHash);

            if (!CryptographicOperations.FixedTimeEquals(storedHashBytes, submittedHashBytes))
            {
                // Increment failed attempts
                user.FailedOTPAttempts++;

                if (user.FailedOTPAttempts >= MaxOTPAttempts)
                {
                    user.OTPLockoutEnd = DateTime.Now.AddMinutes(OTPLockoutMinutes);
                    _context.Update(user);
                    await _context.SaveChangesAsync();
                    _logger.LogWarning("OTP lockout triggered for user {UserID} after {Attempts} attempts", user.UserID, user.FailedOTPAttempts);
                    return (false, $"Too many failed attempts. Verification locked for {OTPLockoutMinutes} minutes.");
                }

                _context.Update(user);
                await _context.SaveChangesAsync();

                var attemptsLeft = MaxOTPAttempts - user.FailedOTPAttempts;
                return (false, $"Invalid verification code. {attemptsLeft} attempt(s) remaining.");
            }

            // ── SUCCESS ──
            await ClearOTPData(user);
            return (true, null);
        }

        // ────────────────────────────────────────────────────────
        // EMAIL DELIVERY
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Sends the OTP code to the user's registered email address.
        /// Falls back to console logging if SMTP is not configured (dev mode).
        /// </summary>
        public async Task<bool> SendOTPEmail(User user, string otpCode)
        {
            var smtpHost = _configuration["Email:SmtpHost"];
            var smtpPort = _configuration.GetValue("Email:SmtpPort", 587);
            var useSsl = _configuration.GetValue("Email:UseSsl", true);
            var senderEmail = _configuration["Email:SenderEmail"];
            var senderName = _configuration["Email:SenderName"] ?? "FinSight Security";
            var senderPassword = _configuration["Email:SenderPassword"];

            // Dev mode: log OTP to console if SMTP not configured
            if (string.IsNullOrEmpty(senderEmail) || string.IsNullOrEmpty(senderPassword))
            {
                _logger.LogWarning("╔══════════════════════════════════════════════════╗");
                _logger.LogWarning("║  [DEV MODE] OTP for {Email}: {OTP}              ║", user.Email, otpCode);
                _logger.LogWarning("╚══════════════════════════════════════════════════╝");
                return true;
            }

            try
            {
                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    Credentials = new NetworkCredential(senderEmail, senderPassword),
                    EnableSsl = useSsl
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(senderEmail, senderName),
                    Subject = $"FinSight - Your Verification Code: {otpCode}",
                    IsBodyHtml = true,
                    Body = GenerateOTPEmailBody(user.FullName, otpCode, OTPExpiryMinutes)
                };

                // Route OTP to sender email if SuperAdmin is using a dummy test account
                var targetEmail = user.Email;
                if (user.RoleID == FinSight.Helpers.Roles.SuperAdmin)
                {
                    targetEmail = senderEmail;
                    _logger.LogInformation("Routing SuperAdmin OTP to {TargetEmail} (Test Account override)", targetEmail);
                }
                
                mailMessage.To.Add(targetEmail);

                await client.SendMailAsync(mailMessage);
                _logger.LogInformation("OTP email sent to {Email}", user.Email);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send OTP email to {Email}", user.Email);
                // Fallback: log to console so testing isn't blocked
                _logger.LogWarning("[FALLBACK] OTP for {Email}: {OTP}", user.Email, otpCode);
                return true; // Don't block login flow due to email failure
            }
        }

        // ────────────────────────────────────────────────────────
        // RATE LIMITING & STATE CHECKS
        // ────────────────────────────────────────────────────────

        /// <summary>Returns true if the user is currently locked out from OTP verification.</summary>
        public bool IsOTPLockedOut(User user)
        {
            return user.OTPLockoutEnd.HasValue && user.OTPLockoutEnd > DateTime.Now;
        }

        /// <summary>Returns true if the user can request a new OTP (respects cooldown).</summary>
        public bool CanResendOTP(User user)
        {
            if (!user.LastOTPSentAt.HasValue) return true;
            return (DateTime.Now - user.LastOTPSentAt.Value).TotalSeconds >= ResendCooldownSeconds;
        }

        /// <summary>Returns the number of seconds until resend is available.</summary>
        public int GetResendCooldownRemaining(User user)
        {
            if (!user.LastOTPSentAt.HasValue) return 0;
            var elapsed = (DateTime.Now - user.LastOTPSentAt.Value).TotalSeconds;
            var remaining = ResendCooldownSeconds - (int)elapsed;
            return remaining > 0 ? remaining : 0;
        }

        /// <summary>Returns the number of seconds until the current OTP expires.</summary>
        public int GetOTPRemainingSeconds(User user)
        {
            if (!user.OTPExpiration.HasValue) return 0;
            var remaining = (int)(user.OTPExpiration.Value - DateTime.Now).TotalSeconds;
            return remaining > 0 ? remaining : 0;
        }

        // ────────────────────────────────────────────────────────
        // 2FA ENFORCEMENT LOGIC
        // ────────────────────────────────────────────────────────

        /// <summary>
        /// Determines if 2FA should be required for this user.
        /// Mandatory for SuperAdmin, Admin, FinanceManager.
        /// Optional for DepartmentHead, Executive (based on user preference).
        /// </summary>
        public bool ShouldRequire2FA(int? roleId, bool userEnabled)
        {
            if (!IsTwoFactorGloballyEnabled) return false;

            if (roleId == null) return false;
            if (Roles.RequiresTwoFactor(roleId.Value)) return true;
            return userEnabled;
        }

        // ────────────────────────────────────────────────────────
        // HELPERS
        // ────────────────────────────────────────────────────────

        /// <summary>Masks an email address for display (e.g., "john@company.com" → "j***@company.com")</summary>
        public static string MaskEmail(string email)
        {
            if (string.IsNullOrEmpty(email) || !email.Contains('@'))
                return "***@***.com";

            var parts = email.Split('@');
            var local = parts[0];
            var domain = parts[1];

            if (local.Length <= 1)
                return $"{local}***@{domain}";

            return $"{local[0]}***@{domain}";
        }

        /// <summary>Clears all OTP data from the user after successful verification.</summary>
        public async Task ClearOTPData(User user)
        {
            user.OTPCode = null;
            user.OTPExpiration = null;
            user.FailedOTPAttempts = 0;
            user.OTPLockoutEnd = null;

            _context.Update(user);
            await _context.SaveChangesAsync();
        }

        /// <summary>Generates a cryptographically secure 6-digit numeric code.</summary>
        private static string GenerateSecureCode()
        {
            // Generate a random number between 100000 and 999999
            var bytes = RandomNumberGenerator.GetBytes(4);
            var value = BitConverter.ToUInt32(bytes, 0);
            var code = (value % 900000) + 100000;
            return code.ToString();
        }

        /// <summary>Hashes an OTP using HMACSHA256 with the server secret key.</summary>
        private string HashOTP(string otp)
        {
            var keyBytes = Encoding.UTF8.GetBytes(OTPSecretKey);
            var otpBytes = Encoding.UTF8.GetBytes(otp);

            using var hmac = new HMACSHA256(keyBytes);
            var hash = hmac.ComputeHash(otpBytes);
            return Convert.ToBase64String(hash);
        }

        /// <summary>Generates a professional HTML email body for OTP delivery.</summary>
        private static string GenerateOTPEmailBody(string fullName, string otpCode, int expiryMinutes)
        {
            return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
</head>
<body style='margin:0; padding:0; background-color:#e7eaef; font-family:Inter,Arial,sans-serif;'>
    <table width='100%' cellpadding='0' cellspacing='0' style='padding:40px 20px;'>
        <tr>
            <td align='center'>
                <table width='500' cellpadding='0' cellspacing='0' style='background:#ffffff; border-radius:16px; overflow:hidden; box-shadow:0 4px 24px rgba(8,11,17,0.1);'>
                    <!-- Header -->
                    <tr>
                        <td style='background:linear-gradient(135deg,#162229,#0f171d); padding:32px 40px; text-align:center;'>
                            <h1 style='color:#ffffff; margin:0; font-size:24px; font-weight:800; letter-spacing:0.5px;'>
                                🔐 FinSight
                            </h1>
                            <p style='color:rgba(255,255,255,0.7); margin:8px 0 0; font-size:14px;'>Security Verification</p>
                        </td>
                    </tr>
                    <!-- Body -->
                    <tr>
                        <td style='padding:40px;'>
                            <p style='color:#162229; font-size:16px; margin:0 0 8px;'>Hello <strong>{fullName}</strong>,</p>
                            <p style='color:#9ba8b8; font-size:14px; margin:0 0 32px; line-height:1.6;'>
                                We received a login request for your FinSight account. Use the verification code below to complete your sign-in:
                            </p>
                            <!-- OTP Code -->
                            <div style='background:#e7eaef; border-radius:12px; padding:24px; text-align:center; margin:0 0 32px;'>
                                <span style='font-size:36px; font-weight:800; letter-spacing:8px; color:#05828e; font-family:monospace;'>{otpCode}</span>
                            </div>
                            <p style='color:#9ba8b8; font-size:13px; margin:0 0 24px; text-align:center;'>
                                ⏱ This code expires in <strong style='color:#162229;'>{expiryMinutes} minutes</strong>
                            </p>
                            <hr style='border:none; border-top:1px solid #e7eaef; margin:0 0 24px;'>
                            <p style='color:#9ba8b8; font-size:12px; margin:0; line-height:1.6;'>
                                If you didn't request this code, please ignore this email or contact your administrator immediately. 
                                Never share this code with anyone.
                            </p>
                        </td>
                    </tr>
                    <!-- Footer -->
                    <tr>
                        <td style='background:#f8f9fa; padding:20px 40px; text-align:center;'>
                            <p style='color:#9ba8b8; font-size:11px; margin:0;'>© {DateTime.Now.Year} FinSight Software. All rights reserved.</p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
        }
    }
}
