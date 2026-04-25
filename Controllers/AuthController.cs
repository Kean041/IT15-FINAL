using FinSight.Data;
using FinSight.Helpers;
using FinSight.Models;
using FinSight.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace FinSight.Controllers
{
    public class AuthController : Controller
    {
        private readonly FinSightDbContext _context;

        public AuthController(FinSightDbContext context)
        {
            _context = context;
        }

        // ── REGISTER GET ────────────────────────────
        [HttpGet]
        public IActionResult Register(string? plan)
        {
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

            // 1. Create the Tenant
            var tenant = new Tenant
            {
                CompanyName = model.CompanyName,
                CreatedDate = DateTime.Now,
                SubscriptionPlan = model.Plan
            };
            _context.Tenants.Add(tenant);
            await _context.SaveChangesAsync();

            // 2. Create the User linked to the Tenant
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

            // Set success message and redirect to Login
            TempData["SuccessMessage"] = "Account created successfully! Please log in.";
            return RedirectToAction(nameof(Login));
        }

        // ── LOGIN GET ───────────────────────────────
        [HttpGet]
        public IActionResult Login()
        {
            return View(new LoginViewModel());
        }

        // ── LOGIN POST ──────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
                return View(model);

            var hashedPassword = PasswordHelper.HashPassword(model.Password);

            var user = await _context.Users
                .Include(u => u.Tenant)
                .FirstOrDefaultAsync(u => u.Email == model.Email && u.PasswordHash == hashedPassword);

            if (user == null)
            {
                ViewBag.Error = "Invalid email or password.";
                return View(model);
            }

            // Store user info in session
            HttpContext.Session.SetInt32("UserID", user.UserID);
            HttpContext.Session.SetString("FullName", user.FullName);
            HttpContext.Session.SetString("Email", user.Email);
            HttpContext.Session.SetInt32("TenantID", user.TenantID ?? 0);
            HttpContext.Session.SetString("CompanyName", user.Tenant?.CompanyName ?? "");
            HttpContext.Session.SetInt32("RoleID", user.RoleID ?? 1);
            HttpContext.Session.SetString("RoleName", Helpers.Roles.GetRoleName(user.RoleID ?? 1));

            if (user.RoleID == Helpers.Roles.SuperAdmin)
            {
                return RedirectToAction("Dashboard", "SuperAdmin");
            }

            return RedirectToAction("Index", "Dashboard");
        }

        // ── LOGOUT ──────────────────────────────────
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }


    }
}
