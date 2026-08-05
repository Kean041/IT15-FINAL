using FinSight.Data;
using FinSight.Helpers;
using FinSight.Models;
using FinSight.Models.ViewModels;
using FinSight.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using OtpNet;
using QRCoder;

namespace FinSight.Controllers
{
    public class AuthController : Controller
    {
        private readonly FinSightDbContext _context;
        private readonly ILogger<AuthController> _logger;
        private readonly AuditLogService _auditLog;
        private readonly NotificationService _notification;
        private readonly IConfiguration _configuration;
        private readonly EmailService _emailService;

        // Lockout configuration
        private const int MaxFailedAttempts = 5;
        private const int LockoutMinutes = 15;

        public AuthController(FinSightDbContext context, ILogger<AuthController> logger, AuditLogService auditLog, NotificationService notification, IConfiguration configuration, EmailService emailService)
        {
            _context = context;
            _logger = logger;
            _auditLog = auditLog;
            _notification = notification;
            _configuration = configuration;
            _emailService = emailService;
        }

        // ── REGISTER GET ────────────────────────────
        [HttpGet]
        public IActionResult Register(string? plan)
        {
            // Clear any existing session to ensure a clean slate for the new user
            HttpContext.Session.Clear();

            var model = new RegisterViewModel { Plan = plan };
            return View(model);
        }

        // ── REGISTER POST ───────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            // Check if email already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == model.Email);

            if (existingUser != null)
            {
                ModelState.AddModelError("Email", "An account with this email already exists.");
                return View(model);
            }

            // Check if company name already exists
            var existingTenant = await _context.Tenants
                .FirstOrDefaultAsync(t => t.CompanyName == model.CompanyName);

            if (existingTenant != null)
            {
                ModelState.AddModelError("CompanyName", "A company with this name has already been registered.");
                return View(model);
            }

            // 1. Create the Tenant with Pending status
            var tenant = new Tenant
            {
                CompanyName = model.CompanyName,
                CreatedDate = DateTime.Now,
                SubscriptionPlan = model.Plan,
                SubscriptionStatus = "Pending" // Explicitly set pending
            };
            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync();

            // 2. Create the Admin User linked to the Tenant (PBKDF2 hash)
            var user = new User
            {
                TenantID = tenant.TenantID,
                FullName = model.FullName,
                Email = model.Email,
                PasswordHash = PasswordHelper.HashPassword(model.Password),
                RoleID = 1, // Admin
                CreatedAt = DateTime.Now
            };
            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            // Audit: Registration
            await _auditLog.LogSystemAction(tenant.TenantID, user.UserID,
                "UserRegistered", $"New tenant '{model.CompanyName}' registered by {model.Email} with plan '{model.Plan}'.", GetIP());

            // Auto-login: Store user info in session immediately
            SetUserSession(user, tenant);

            // 4. Redirect directly to subscription/payment page
            return RedirectToAction("Pending", "Subscription");
        }

        // ── LOGIN GET ───────────────────────────────
        [HttpGet]
        public IActionResult Login()
        {
            // If user is already fully logged in, redirect them
            if (HttpContext.Session.GetInt32("UserID") != null)
            {
                var subscriptionStatus = HttpContext.Session.GetString("SubscriptionStatus");
                if (subscriptionStatus == "Pending")
                    return RedirectToAction("Pending", "Subscription");
                else
                    return RedirectToAction("Index", "Dashboard");
            }

            // If user is already in a pending 2FA state and clicks Login, clear the pending state
            if (HttpContext.Session.GetInt32("Pending2FA_UserID") != null)
            {
                HttpContext.Session.Remove("Pending2FA_UserID");
                HttpContext.Session.Remove("Pending2FA_Email");
            }

            return View(new LoginViewModel());
        }

        // ── LOGIN POST ──────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            try
            {
                return await ProcessLoginAsync(model);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed with a server error for {Email}.", model.Email);
                ViewBag.Error = "Unable to sign in right now. Please check the deployed database configuration and try again.";
                return View(model);
            }
        }

        private async Task<IActionResult> ProcessLoginAsync(LoginViewModel model)
        {
            var loginEmail = model.Email.Trim();

            // Find user by email only (don't expose whether email exists)
            var user = await _context.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Email == loginEmail);

            user = await RepairConfiguredAdminForLoginAsync(model, user);

            if (user == null)
            {
                ViewBag.Error = "Invalid email or password.";
                return View(model);
            }

            // ── Account Lockout Check ──
            if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.Now)
            {
                var remaining = (int)Math.Ceiling((user.LockoutEnd.Value - DateTime.Now).TotalMinutes);
                ViewBag.Error = $"Account is temporarily locked. Try again in {remaining} minute(s).";
                _logger.LogWarning("Locked account login attempt for {Email}", user.Email);
                return View(model);
            }

            // ── Verify Password (supports both PBKDF2 and legacy SHA256) ──
            if (!PasswordHelper.VerifyPassword(model.Password, user.PasswordHash))
            {
                // Increment failed attempts
                user.FailedLoginAttempts++;

                if (user.FailedLoginAttempts >= MaxFailedAttempts)
                {
                    user.LockoutEnd = DateTime.Now.AddMinutes(LockoutMinutes);
                    _logger.LogWarning("Account locked for {Email} after {Attempts} failed attempts",
                        user.Email, user.FailedLoginAttempts);

                    // Audit: Account Lockout
                    await _auditLog.LogSecurityAction(user.TenantID, user.UserID,
                        "AccountLocked", $"Account locked after {user.FailedLoginAttempts} failed attempts.", GetIP(), "Critical");

                    // Notify Super Admin
                    await _notification.CreateNotificationAsync(null, null, "Security", 
                        "Account Locked", 
                        $"Account {user.Email} locked after {user.FailedLoginAttempts} failed attempts.", 
                        null);
                }

                _context.Update(user);
                await _context.SaveChangesAsync();

                // Audit: Failed Login
                await _auditLog.LogSecurityAction(user.TenantID, user.UserID,
                    "LoginFailed", $"Failed login attempt for {user.Email}. Attempt #{user.FailedLoginAttempts}.", GetIP(), "Warning");

                // Notify User/Tenant Admin (optional, but requested by requirements)
                await _notification.CreateNotificationAsync(user.TenantID, user.UserID, "Security", 
                    "Failed Login Attempt", 
                    $"A failed login attempt was detected for your account.", 
                    null);

                ViewBag.Error = "Invalid email or password.";
                return View(model);
            }

            // ── Password Verified Successfully ──

            // Auto-upgrade legacy SHA256 hash to PBKDF2
            if (PasswordHelper.IsLegacyHash(user.PasswordHash))
            {
                user.PasswordHash = PasswordHelper.HashPassword(model.Password);
                _logger.LogInformation("Auto-upgraded password hash for user {UserID}", user.UserID);
            }

            // Reset lockout counters
            user.FailedLoginAttempts = 0;
            user.LockoutEnd = null;
            _context.Update(user);
            await _context.SaveChangesAsync();

            // ── 2FA Check ──
            bool is2FaGloballyEnabled = _configuration.GetValue<bool>("TwoFactor:Enabled");
            // Temporarily disable OTP Check
            /*
            if (user.IsTwoFactorEnabled || is2FaGloballyEnabled)
            {
                HttpContext.Session.SetInt32("Pending2FA_UserID", user.UserID);
                HttpContext.Session.SetString("Pending2FA_Email", user.Email);

                // Generate and send email OTP
                await GenerateAndSendOTP(user);

                return RedirectToAction("VerifyEmailOTP");
            }
            */

            // Complete Login Immediately
            return await CompleteLogin(user);
        }

        private async Task<User?> RepairConfiguredAdminForLoginAsync(LoginViewModel model, User? user)
        {
            var configuredEmail = _configuration["SeedUsers:AdminEmail"]?.Trim();
            var configuredPassword = _configuration["SeedUsers:AdminPassword"];

            if (string.IsNullOrWhiteSpace(configuredEmail) || string.IsNullOrEmpty(configuredPassword))
                return user;

            if (!string.Equals(model.Email.Trim(), configuredEmail, StringComparison.OrdinalIgnoreCase))
                return user;

            var passwordMatchesExistingUser = user != null && PasswordHelper.VerifyPassword(model.Password, user.PasswordHash);
            var passwordMatchesConfiguredSecret =
                string.Equals(model.Password, configuredPassword, StringComparison.Ordinal) ||
                string.Equals(model.Password, configuredPassword.Trim(), StringComparison.Ordinal);

            if (!passwordMatchesExistingUser && !passwordMatchesConfiguredSecret)
                return user;

            var companyName = _configuration["SeedUsers:AdminCompanyName"];
            if (string.IsNullOrWhiteSpace(companyName))
                companyName = "FinSight Demo";

            var adminName = _configuration["SeedUsers:AdminName"];
            if (string.IsNullOrWhiteSpace(adminName))
                adminName = "Demo Admin";

            var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.CompanyName == companyName);
            if (tenant == null)
            {
                tenant = new Tenant
                {
                    CompanyName = companyName,
                    CreatedDate = DateTime.Now,
                    SubscriptionPlan = "Enterprise",
                    SubscriptionStatus = "Active"
                };

                _context.Tenants.Add(tenant);
                await _context.SaveChangesAsync();
            }
            else
            {
                tenant.SubscriptionPlan ??= "Enterprise";
                tenant.SubscriptionStatus = "Active";
            }

            if (user == null)
            {
                user = new User
                {
                    TenantID = tenant.TenantID,
                    FullName = adminName,
                    Email = configuredEmail,
                    PasswordHash = PasswordHelper.HashPassword(model.Password),
                    RoleID = Helpers.Roles.Admin,
                    IsArchived = false,
                    IsTwoFactorEnabled = false,
                    FailedLoginAttempts = 0,
                    LockoutEnd = null,
                    CreatedAt = DateTime.Now,
                    Tenant = tenant
                };

                _context.Users.Add(user);
            }
            else
            {
                user.TenantID = tenant.TenantID;
                user.Tenant = tenant;
                user.FullName = string.IsNullOrWhiteSpace(user.FullName) ? adminName : user.FullName;
                user.Email = configuredEmail;
                user.RoleID = Helpers.Roles.Admin;
                user.IsArchived = false;
                user.IsTwoFactorEnabled = false;
                user.FailedLoginAttempts = 0;
                user.LockoutEnd = null;
                user.CreatedAt ??= DateTime.Now;

                if (!passwordMatchesExistingUser)
                    user.PasswordHash = PasswordHelper.HashPassword(model.Password);

                _context.Users.Update(user);
            }

            await _context.SaveChangesAsync();
            return user;
        }

        private async Task GenerateAndSendOTP(User user)
        {
            // Invalidate any existing OTPs for the user
            var existingOtps = await _context.UserOTPs
                .Where(o => o.UserID == user.UserID && !o.IsUsed && !o.IsExpired)
                .ToListAsync();
                
            foreach (var existing in existingOtps)
            {
                existing.IsExpired = true;
            }
            
            // Generate 6 digit code
            string code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            
            // Store hashed OTP
            var userOtp = new UserOTP
            {
                UserID = user.UserID,
                TenantID = user.TenantID,
                OTPHash = PasswordHelper.HashPassword(code),
                GeneratedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddSeconds(60),
                CreatedByIP = GetIP(),
                IsUsed = false,
                AttemptCount = 0,
                IsExpired = false
            };
            
            _context.UserOTPs.Add(userOtp);
            await _context.SaveChangesAsync();
            
            // Send email
            string subject = "FinSight Multi-Factor Authentication Code";
            string body = $"Hello {user.FullName},\n\nYour FinSight verification code is:\n\n{code}\n\nThis code will expire in 60 seconds.\n\nIf you did not attempt to sign in, please ignore this email and consider changing your password.\n\nThank you,\nFinSight Security Team";
            
            await _emailService.SendEmailAsync(user.Email, subject, body);
            
            // Audit Log
            await _auditLog.LogSecurityAction(user.TenantID, user.UserID, "OTPSent", $"OTP Sent to {user.Email}", GetIP(), "Info");
            await _notification.CreateNotificationAsync(user.TenantID, user.UserID, "Security", "OTP Sent", "An OTP was sent to your email address.", null);
        }

        // ── VERIFY EMAIL OTP GET ─────────────────────────────
        [HttpGet]
        public IActionResult VerifyEmailOTP()
        {
            var pendingUserId = HttpContext.Session.GetInt32("Pending2FA_UserID");
            if (pendingUserId == null)
                return RedirectToAction(nameof(Login));

            var model = new EmailMfaViewModel 
            { 
                Email = HttpContext.Session.GetString("Pending2FA_Email") 
            };
            return View(model);
        }

        // ── VERIFY EMAIL OTP POST ────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyEmailOTP(EmailMfaViewModel model)
        {
            var pendingUserId = HttpContext.Session.GetInt32("Pending2FA_UserID");
            if (pendingUserId == null)
                return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid)
                return View(model);

            var user = await _context.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.UserID == pendingUserId.Value);
            if (user == null)
                return RedirectToAction(nameof(Login));

            // Check if user is locked out from MFA
            if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTime.Now)
            {
                ModelState.AddModelError("", $"Account is temporarily locked. Try again later.");
                return View(model);
            }

            var activeOtp = await _context.UserOTPs
                .Where(o => o.UserID == user.UserID && !o.IsUsed && !o.IsExpired)
                .OrderByDescending(o => o.GeneratedAt)
                .FirstOrDefaultAsync();

            if (activeOtp == null || activeOtp.ExpiresAt < DateTime.Now)
            {
                if (activeOtp != null && activeOtp.ExpiresAt < DateTime.Now)
                {
                    activeOtp.IsExpired = true;
                    await _context.SaveChangesAsync();
                    await _auditLog.LogSecurityAction(user.TenantID, user.UserID, "OTPExpired", "OTP code expired before use.", GetIP(), "Warning");
                }
                
                ModelState.AddModelError("", "The code has expired or is invalid. Please request a new one.");
                return View(model);
            }

            // Verify Code
            if (PasswordHelper.VerifyPassword(model.Code, activeOtp.OTPHash))
            {
                // Success
                activeOtp.IsUsed = true;
                activeOtp.UsedAt = DateTime.Now;
                await _context.SaveChangesAsync();

                HttpContext.Session.Remove("Pending2FA_UserID");
                HttpContext.Session.Remove("Pending2FA_Email");

                await _auditLog.LogSecurityAction(user.TenantID, user.UserID, "OTPVerificationSuccess", "Successfully verified OTP code.", GetIP(), "Info");
                return await CompleteLogin(user);
            }

            // Failed Verification
            activeOtp.AttemptCount++;
            if (activeOtp.AttemptCount >= MaxFailedAttempts)
            {
                activeOtp.IsExpired = true; // Invalidate current OTP
                user.LockoutEnd = DateTime.Now.AddMinutes(LockoutMinutes); // Lockout MFA
                await _context.SaveChangesAsync();

                await _auditLog.LogSecurityAction(user.TenantID, user.UserID, "AccountLocked", $"MFA Account locked after {activeOtp.AttemptCount} failed OTP attempts.", GetIP(), "Critical");
                await _notification.CreateNotificationAsync(user.TenantID, user.UserID, "Security", "Account Locked", "Your account was locked due to too many failed MFA attempts.", null);

                ModelState.AddModelError("", "Too many failed attempts. Your account has been temporarily locked.");
                return View(model);
            }

            await _context.SaveChangesAsync();
            await _auditLog.LogSecurityAction(user.TenantID, user.UserID, "OTPVerificationFailed", $"Failed OTP verification attempt. Attempt #{activeOtp.AttemptCount}.", GetIP(), "Warning");

            model.RemainingAttempts = MaxFailedAttempts - activeOtp.AttemptCount;
            ModelState.AddModelError("Code", $"Invalid verification code. {model.RemainingAttempts} attempts remaining.");
            return View(model);
        }

        // ── RESEND EMAIL OTP POST ────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendEmailOTP()
        {
            var pendingUserId = HttpContext.Session.GetInt32("Pending2FA_UserID");
            if (pendingUserId == null)
                return RedirectToAction(nameof(Login));

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == pendingUserId.Value);
            if (user == null)
                return RedirectToAction(nameof(Login));

            // Prevent spamming resends (limit 3 per 15 minutes)
            var recentOtps = await _context.UserOTPs
                .Where(o => o.UserID == user.UserID && o.GeneratedAt > DateTime.Now.AddMinutes(-15))
                .CountAsync();

            if (recentOtps >= 3)
            {
                TempData["ErrorMessage"] = "You have requested too many codes recently. Please wait a while before requesting a new one.";
                return RedirectToAction(nameof(VerifyEmailOTP));
            }

            await GenerateAndSendOTP(user);
            await _auditLog.LogSecurityAction(user.TenantID, user.UserID, "OTPResent", "User requested a new OTP code.", GetIP(), "Info");

            TempData["SuccessMessage"] = "A new verification code has been sent to your email.";
            return RedirectToAction(nameof(VerifyEmailOTP));
        }



        // ── LOGOUT ──────────────────────────────────
        public async Task<IActionResult> Logout()
        {
            var userId = HttpContext.Session.GetInt32("UserID");
            var tenantId = HttpContext.Session.GetInt32("TenantID");
            var name = HttpContext.Session.GetString("FullName") ?? "Unknown";

            // Audit: Logout
            await _auditLog.LogSecurityAction(tenantId, userId,
                "Logout", $"{name} logged out.", GetIP());

            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }

        // ════════════════════════════════════════════════════════
        // PRIVATE HELPERS
        // ════════════════════════════════════════════════════════

        /// <summary>
        /// Completes the login flow after password + optional 2FA verification.
        /// Sets full session and redirects to the appropriate dashboard.
        /// </summary>
        private async Task<IActionResult> CompleteLogin(User user)
        {
            // Reload tenant if needed
            if (user.Tenant == null && user.TenantID.HasValue)
            {
                user.Tenant = await _context.Tenants.FindAsync(user.TenantID.Value);
            }

            // Store full user session
            SetUserSession(user, user.Tenant);

            // Audit: Login Success
            await _auditLog.LogSecurityAction(user.TenantID, user.UserID,
                "LoginSuccess", $"{user.FullName} logged in successfully.", GetIP());

            // SuperAdmin bypasses subscription checks
            if (user.RoleID == Helpers.Roles.SuperAdmin)
            {
                return RedirectToAction("Dashboard", "SuperAdmin");
            }

            // Check subscription status — redirect to payment if not active
            var subscriptionStatus = user.Tenant?.SubscriptionStatus ?? "Pending";
            if (subscriptionStatus != "Active")
            {
                return RedirectToAction("Pending", "Subscription");
            }

            return RedirectToAction("Index", "Dashboard");
        }

        /// <summary>Stores user info in session for authenticated access.</summary>
        private void SetUserSession(User user, Tenant? tenant)
        {
            HttpContext.Session.SetInt32("UserID", user.UserID);
            HttpContext.Session.SetString("FullName", string.IsNullOrWhiteSpace(user.FullName) ? "Admin User" : user.FullName);
            HttpContext.Session.SetString("Email", user.Email ?? string.Empty);
            HttpContext.Session.SetInt32("TenantID", tenant?.TenantID ?? 0);
            HttpContext.Session.SetString("CompanyName", tenant?.CompanyName ?? "");
            HttpContext.Session.SetString("SubscriptionStatus", tenant?.SubscriptionStatus ?? "Pending");
            HttpContext.Session.SetString("SubscriptionPlan", tenant?.SubscriptionPlan ?? "Basic");
            HttpContext.Session.SetInt32("RoleID", user.RoleID ?? 1);
            HttpContext.Session.SetString("RoleName", Helpers.Roles.GetRoleName(user.RoleID ?? 1));
            if (user.DepartmentID.HasValue)
                HttpContext.Session.SetInt32("DepartmentID", user.DepartmentID.Value);
        }

        private string? GetIP() => HttpContext.Connection.RemoteIpAddress?.ToString();
    }
}
