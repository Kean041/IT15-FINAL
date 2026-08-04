using FinSight.Data;
using FinSight.Helpers;
using FinSight.Models;
using FinSight.Models.ViewModels;
using FinSight.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using System.Text.Json;
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

        // Lockout configuration
        private const int MaxFailedAttempts = 5;
        private const int LockoutMinutes = 15;

        public AuthController(FinSightDbContext context, ILogger<AuthController> logger, AuditLogService auditLog, NotificationService notification, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _auditLog = auditLog;
            _notification = notification;
            _configuration = configuration;
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

            // Verify Cloudflare Turnstile
            var turnstileToken = Request.Form["cf-turnstile-response"].ToString();
            if (!await VerifyTurnstileTokenAsync(turnstileToken))
            {
                ModelState.AddModelError("", "Security check failed. Please verify you are a human.");
                return View(model);
            }

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

            // Verify Cloudflare Turnstile
            var turnstileToken = Request.Form["cf-turnstile-response"].ToString();
            if (!await VerifyTurnstileTokenAsync(turnstileToken))
            {
                ViewBag.Error = "Security check failed. Please verify you are a human.";
                return View(model);
            }

            // Find user by email only (don't expose whether email exists)
            var user = await _context.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Email == model.Email);

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
            if (user.IsTwoFactorEnabled || is2FaGloballyEnabled)
            {
                HttpContext.Session.SetInt32("Pending2FA_UserID", user.UserID);
                HttpContext.Session.SetString("Pending2FA_Email", user.Email);

                if (string.IsNullOrEmpty(user.TwoFactorSecretKey))
                {
                    // Needs to set up authenticator
                    return RedirectToAction("SetupAuthenticator");
                }

                // Needs to verify code
                return RedirectToAction("VerifyAuthenticator");
            }

            // Complete Login Immediately
            return await CompleteLogin(user);
        }

        // ── SETUP AUTHENTICATOR GET ──────────────────────────────
        [HttpGet]
        public IActionResult SetupAuthenticator()
        {
            var pendingUserId = HttpContext.Session.GetInt32("Pending2FA_UserID");
            if (pendingUserId == null)
                return RedirectToAction(nameof(Login));

            // Generate new secret key
            var secretKey = KeyGeneration.GenerateRandomKey(20);
            var base32Secret = Base32Encoding.ToString(secretKey);

            var pendingEmail = HttpContext.Session.GetString("Pending2FA_Email") ?? "User";
            var issuer = "FinSight";

            // Generate QR Code URI
            var uriString = $"otpauth://totp/{issuer}:{pendingEmail}?secret={base32Secret}&issuer={issuer}&digits=6";

            var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(uriString, QRCodeGenerator.ECCLevel.Q);
            var qrCode = new PngByteQRCode(qrCodeData);
            var qrCodeImage = qrCode.GetGraphic(20);
            var base64QrCode = Convert.ToBase64String(qrCodeImage);

            var model = new TwoFactorViewModel
            {
                SecretKey = base32Secret,
                QrCodeUri = $"data:image/png;base64,{base64QrCode}"
            };

            return View(model);
        }

        // ── SETUP AUTHENTICATOR POST ─────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetupAuthenticator(TwoFactorViewModel model)
        {
            var pendingUserId = HttpContext.Session.GetInt32("Pending2FA_UserID");
            if (pendingUserId == null)
                return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid)
                return View(model);

            var totp = new Totp(Base32Encoding.ToBytes(model.SecretKey));
            if (totp.VerifyTotp(model.Code, out long timeStepMatched, new VerificationWindow(2, 2)))
            {
                var user = await _context.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.UserID == pendingUserId.Value);
                if (user != null)
                {
                    user.TwoFactorSecretKey = model.SecretKey;
                    user.IsTwoFactorEnabled = true;
                    _context.Update(user);
                    await _context.SaveChangesAsync();

                    HttpContext.Session.Remove("Pending2FA_UserID");
                    HttpContext.Session.Remove("Pending2FA_Email");

                    // Audit: 2FA Setup
                    await _auditLog.LogSecurityAction(user.TenantID, user.UserID,
                        "TwoFactorSetup", $"User {user.Email} successfully set up 2FA.", GetIP(), "Info");

                    return await CompleteLogin(user);
                }
            }

            ModelState.AddModelError("Code", "Invalid verification code.");
            return View(model);
        }

        // ── VERIFY AUTHENTICATOR GET ─────────────────────────────
        [HttpGet]
        public IActionResult VerifyAuthenticator()
        {
            var pendingUserId = HttpContext.Session.GetInt32("Pending2FA_UserID");
            if (pendingUserId == null)
                return RedirectToAction(nameof(Login));

            return View(new TwoFactorViewModel());
        }

        // ── VERIFY AUTHENTICATOR POST ────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyAuthenticator(TwoFactorViewModel model)
        {
            var pendingUserId = HttpContext.Session.GetInt32("Pending2FA_UserID");
            if (pendingUserId == null)
                return RedirectToAction(nameof(Login));

            if (!ModelState.IsValid)
                return View(model);

            var user = await _context.Users.Include(u => u.Tenant).FirstOrDefaultAsync(u => u.UserID == pendingUserId.Value);
            if (user == null || string.IsNullOrEmpty(user.TwoFactorSecretKey))
            {
                return RedirectToAction(nameof(Login));
            }

            var totp = new Totp(Base32Encoding.ToBytes(user.TwoFactorSecretKey));
            if (totp.VerifyTotp(model.Code, out long timeStepMatched, new VerificationWindow(2, 2)))
            {
                HttpContext.Session.Remove("Pending2FA_UserID");
                HttpContext.Session.Remove("Pending2FA_Email");

                return await CompleteLogin(user);
            }

            ModelState.AddModelError("Code", "Invalid authenticator code.");
            // Audit: Failed 2FA
            await _auditLog.LogSecurityAction(user.TenantID, user.UserID,
                "TwoFactorFailed", $"Failed 2FA verification attempt for {user.Email}.", GetIP(), "Warning");

            return View(model);
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
            HttpContext.Session.SetString("FullName", user.FullName);
            HttpContext.Session.SetString("Email", user.Email);
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

        // ── Cloudflare Turnstile Helper ─────────────
        private async Task<bool> VerifyTurnstileTokenAsync(string token)
        {
            if (string.IsNullOrEmpty(token)) return false;

            var secretKey = _configuration["Cloudflare:TurnstileSecretKey"];
            if (string.IsNullOrEmpty(secretKey)) return true; // Skip if no key (dev mode bypass)

            using var client = new HttpClient();
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("secret", secretKey),
                new KeyValuePair<string, string>("response", token),
                new KeyValuePair<string, string>("remoteip", GetIP() ?? "")
            });

            var response = await client.PostAsync("https://challenges.cloudflare.com/turnstile/v0/siteverify", content);
            if (response.IsSuccessStatusCode)
            {
                var jsonString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonString);
                return doc.RootElement.GetProperty("success").GetBoolean();
            }
            return false;
        }
    }
}
