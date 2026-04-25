using FinSight.Data;
using FinSight.Helpers;
using FinSight.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace FinSight.Controllers
{
    /// <summary>
    /// Manages users within the current tenant.
    /// Only Admin and Super Admin can access this module.
    /// </summary>
    public class UserController : BaseController
    {
        private readonly FinSightDbContext _context;

        public UserController(FinSightDbContext context)
        {
            _context = context;
        }

        // ─────────────────────────────────────────────
        // GET: User/Index  — Active Users
        // ─────────────────────────────────────────────
        public async Task<IActionResult> Index(string searchString, int page = 1)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!HasRole(Roles.SuperAdmin, Roles.Admin)) return AccessDenied();

            int? tenantFilter = GetTenantFilter();
            int pageSize = 10;

            var query = _context.Users
                .Include(u => u.Department)
                .Include(u => u.Tenant)
                .Where(u => u.IsArchived == false)
                .AsQueryable();

            // Apply tenant filter (Super Admin sees all)
            if (tenantFilter != null)
            {
                query = query.Where(u => u.TenantID == tenantFilter.Value);
            }

            // Text search filtering
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(u =>
                    u.FullName.Contains(searchString) ||
                    u.Email.Contains(searchString));
            }

            var orderedQuery = query.OrderByDescending(u => u.CreatedAt);
            var allResults = await orderedQuery.ToListAsync();

            // Analytics summary
            int totalUsersInTenant = await _context.Users
                .Where(u => tenantFilter == null || u.TenantID == tenantFilter.Value)
                .CountAsync();
            int activeCount = await _context.Users
                .Where(u => (tenantFilter == null || u.TenantID == tenantFilter.Value) && u.IsArchived == false)
                .CountAsync();
            int archivedCount = await _context.Users
                .Where(u => (tenantFilter == null || u.TenantID == tenantFilter.Value) && u.IsArchived == true)
                .CountAsync();

            ViewBag.TotalUsers = totalUsersInTenant;
            ViewBag.ActiveCount = activeCount;
            ViewBag.ArchivedCount = archivedCount;

            // Pagination
            int totalRecords = allResults.Count;
            int totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            if (totalPages == 0) totalPages = 1;

            var pagedData = allResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            // Populate dropdowns
            await PopulateDropdowns(tenantFilter);

            // Pass current user ID to prevent self-archive
            ViewBag.CurrentUserID = CurrentUserID;

            return View(pagedData);
        }

        // ─────────────────────────────────────────────
        // GET: User/Archived  — Archived Users
        // ─────────────────────────────────────────────
        public async Task<IActionResult> Archived(string searchString, int page = 1)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!HasRole(Roles.SuperAdmin, Roles.Admin)) return AccessDenied();

            int? tenantFilter = GetTenantFilter();
            int pageSize = 10;

            var query = _context.Users
                .Include(u => u.Department)
                .Include(u => u.Tenant)
                .Where(u => u.IsArchived == true)
                .AsQueryable();

            if (tenantFilter != null)
            {
                query = query.Where(u => u.TenantID == tenantFilter.Value);
            }

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(u =>
                    u.FullName.Contains(searchString) ||
                    u.Email.Contains(searchString));
            }

            var orderedQuery = query.OrderByDescending(u => u.UpdatedAt ?? u.CreatedAt);
            var allResults = await orderedQuery.ToListAsync();

            // Pagination
            int totalRecords = allResults.Count;
            int totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            if (totalPages == 0) totalPages = 1;

            var pagedData = allResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            return View(pagedData);
        }

        // ─────────────────────────────────────────────
        // POST: User/Create
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string FullName, string Email, string Password, int? RoleID, int? DepartmentID)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!HasRole(Roles.SuperAdmin, Roles.Admin)) return AccessDenied();

            // Validate required fields
            if (string.IsNullOrWhiteSpace(FullName) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrWhiteSpace(Password))
            {
                TempData["Error"] = "Full Name, Email, and Password are required.";
                return RedirectToAction(nameof(Index));
            }

            // Check email uniqueness
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == Email.Trim());

            if (existingUser != null)
            {
                TempData["Error"] = "A user with this email already exists.";
                return RedirectToAction(nameof(Index));
            }

            // Auto-assign TenantID from session
            int tenantId = CurrentTenantID ?? 0;
            int assignedRole = RoleID ?? Roles.Admin;

            // Prevent assigning system roles via UI
            if (Roles.IsSystemRole(assignedRole))
            {
                TempData["Error"] = "System roles (Admin, Super Admin) cannot be manually assigned.";
                return RedirectToAction(nameof(Index));
            }

            var user = new User
            {
                FullName = FullName.Trim(),
                Email = Email.Trim(),
                PasswordHash = PasswordHelper.HashPassword(Password),
                RoleID = assignedRole,
                DepartmentID = DepartmentID,
                TenantID = tenantId,
                IsArchived = false,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"User \"{user.FullName}\" created successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────
        // POST: User/Edit
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string FullName, int? RoleID, int? DepartmentID)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!HasRole(Roles.SuperAdmin, Roles.Admin)) return AccessDenied();

            int? tenantFilter = GetTenantFilter();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserID == id && (tenantFilter == null || u.TenantID == tenantFilter.Value));

            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(FullName))
            {
                TempData["Error"] = "Full Name is required.";
                return RedirectToAction(nameof(Index));
            }

            int updatedRole = RoleID ?? user.RoleID ?? Roles.FinanceManager;

            // Prevent assigning system roles via UI during Edit
            if (updatedRole != user.RoleID && Roles.IsSystemRole(updatedRole))
            {
                TempData["Error"] = "System roles (Admin, Super Admin) cannot be manually assigned.";
                return RedirectToAction(nameof(Index));
            }

            user.FullName = FullName.Trim();
            user.RoleID = updatedRole;
            user.DepartmentID = DepartmentID;
            user.UpdatedAt = DateTime.Now;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"User \"{user.FullName}\" updated successfully.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────
        // POST: User/Archive
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!HasRole(Roles.SuperAdmin, Roles.Admin)) return AccessDenied();

            // Prevent self-archive
            if (id == CurrentUserID)
            {
                TempData["Error"] = "You cannot archive your own account.";
                return RedirectToAction(nameof(Index));
            }

            int? tenantFilter = GetTenantFilter();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserID == id && (tenantFilter == null || u.TenantID == tenantFilter.Value));

            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction(nameof(Index));
            }

            user.IsArchived = true;
            user.UpdatedAt = DateTime.Now;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"User \"{user.FullName}\" has been archived.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────
        // POST: User/Restore
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Restore(int id)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!HasRole(Roles.SuperAdmin, Roles.Admin)) return AccessDenied();

            int? tenantFilter = GetTenantFilter();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.UserID == id && (tenantFilter == null || u.TenantID == tenantFilter.Value));

            if (user == null)
            {
                TempData["Error"] = "User not found.";
                return RedirectToAction(nameof(Archived));
            }

            user.IsArchived = false;
            user.UpdatedAt = DateTime.Now;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"User \"{user.FullName}\" has been restored.";
            return RedirectToAction(nameof(Archived));
        }

        // ─────────────────────────────────────────────
        // POST: User/AddDepartmentApi (AJAX)
        // ─────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> AddDepartmentApi([FromBody] string departmentName)
        {
            if (!IsAuthenticated) return Unauthorized();
            if (!HasRole(Roles.SuperAdmin, Roles.Admin)) return Forbid();

            if (string.IsNullOrWhiteSpace(departmentName))
                return BadRequest("Department name is required.");

            int tenantId = CurrentTenantID ?? 0;
            string trimmedName = departmentName.Trim();

            var dept = await _context.Departments
                .FirstOrDefaultAsync(d => d.DepartmentName == trimmedName && d.TenantID == tenantId);

            if (dept == null)
            {
                dept = new Department { DepartmentName = trimmedName, TenantID = tenantId };
                _context.Departments.Add(dept);
                await _context.SaveChangesAsync();
            }

            return Json(new { id = dept.DepartmentID, name = dept.DepartmentName });
        }



        // ─────────────────────────────────────────────
        // Helper: Populate Role + Department Dropdowns
        // ─────────────────────────────────────────────
        private async Task PopulateDropdowns(int? tenantFilter)
        {
            // Roles dropdown (excluding system roles)
            ViewBag.Roles = new List<SelectListItem>
            {
                new SelectListItem { Value = Roles.FinanceManager.ToString(), Text = "Finance Manager" },
                new SelectListItem { Value = Roles.DepartmentHead.ToString(), Text = "Department Head" },
                new SelectListItem { Value = Roles.Executive.ToString(), Text = "Executive" }
            };

            // Departments dropdown (filtered by tenant)
            var deptQuery = _context.Departments.AsQueryable();
            if (tenantFilter != null)
                deptQuery = deptQuery.Where(d => d.TenantID == tenantFilter.Value);

            ViewBag.Departments = await deptQuery
                .OrderBy(d => d.DepartmentName)
                .Select(d => new SelectListItem
                {
                    Value = d.DepartmentID.ToString(),
                    Text = d.DepartmentName
                }).ToListAsync();
        }
    }
}
