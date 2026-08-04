using FinSight.Data;
using FinSight.Helpers;
using FinSight.Models;
using FinSight.Models.ViewModels;
using FinSight.Services;
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
        private readonly AuditLogService _auditLog;
        private readonly NotificationService _notification;

        public UserController(FinSightDbContext context, AuditLogService auditLog, NotificationService notification)
        {
            _context = context;
            _auditLog = auditLog;
            _notification = notification;
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
                .Where(u => !u.IsArchived)
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
                .Where(u => (tenantFilter == null || u.TenantID == tenantFilter.Value) && !u.IsArchived)
                .CountAsync();
            int archivedCount = await _context.Users
                .Where(u => (tenantFilter == null || u.TenantID == tenantFilter.Value) && u.IsArchived)
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
                .Where(u => u.IsArchived)
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

            // Audit: User Created
            await _auditLog.LogSystemAction(tenantId, CurrentUserID,
                "UserCreated", $"User '{user.FullName}' ({user.Email}) created with role '{Roles.GetRoleName(assignedRole)}'.",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            // Notify Admin
            await _notification.CreateNotificationAsync(tenantId, null, "System", 
                "User Created", 
                $"New user '{user.FullName}' was created.", 
                "/User");

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

            // Audit: User Updated
            await _auditLog.LogSystemAction(CurrentTenantID, CurrentUserID,
                "UserUpdated", $"User '{user.FullName}' (ID: {user.UserID}) updated.",
                HttpContext.Connection.RemoteIpAddress?.ToString());

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

            // Audit: User Archived
            await _auditLog.LogSystemAction(CurrentTenantID, CurrentUserID,
                "UserArchived", $"User '{user.FullName}' ({user.Email}) archived.",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            // Notify Admin
            await _notification.CreateNotificationAsync(user.TenantID, null, "System", 
                "User Archived", 
                $"User '{user.FullName}' has been archived.", 
                "/User/Archived");

            TempData["Success"] = $"User \"{user.FullName}\" archived successfully.";
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

            // Audit: User Restored
            await _auditLog.LogSystemAction(CurrentTenantID, CurrentUserID,
                "UserRestored", $"User '{user.FullName}' (ID: {user.UserID}) restored.",
                HttpContext.Connection.RemoteIpAddress?.ToString());

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
        // POST: User/ToggleTwoFactor  — Admin 2FA toggle
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleTwoFactor(int id)
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

            var roleId = user.RoleID ?? 1;

            // Prevent disabling 2FA for mandatory roles
            if (Roles.RequiresTwoFactor(roleId) && user.IsTwoFactorEnabled)
            {
                TempData["Error"] = $"2FA is mandatory for {Roles.GetRoleName(roleId)} and cannot be disabled.";
                return RedirectToAction(nameof(Index));
            }

            // Toggle 2FA
            user.IsTwoFactorEnabled = !user.IsTwoFactorEnabled;



            user.UpdatedAt = DateTime.Now;
            _context.Update(user);
            await _context.SaveChangesAsync();

            var action = user.IsTwoFactorEnabled ? "enabled" : "disabled";

            // Audit
            await _auditLog.LogSecurityAction(user.TenantID, CurrentUserID,
                user.IsTwoFactorEnabled ? "TwoFactorEnabled" : "TwoFactorDisabled",
                $"2FA {action} for user '{user.FullName}' by admin.",
                HttpContext.Connection.RemoteIpAddress?.ToString(),
                user.IsTwoFactorEnabled ? "Info" : "Warning");

            // Notify the affected user
            await _notification.CreateNotificationAsync(user.TenantID, user.UserID, "Security",
                $"Two-Factor Authentication {(user.IsTwoFactorEnabled ? "Enabled" : "Disabled")}",
                $"An administrator has {action} Two-Factor Authentication on your account.",
                null);

            TempData["Success"] = $"2FA has been {action} for \"{user.FullName}\".";
            return RedirectToAction(nameof(Index));
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
        // ═════════════════════════════════════════════
        // AUDIT LOGS — SPLIT INTO SYSTEM & SECURITY
        // ═════════════════════════════════════════════

        public async Task<IActionResult> SystemLogs(
            string? severity, string? search,
            DateTime? startDate, DateTime? endDate, int page = 1)
        {
            return await BuildAuditLogView("System", severity, search, startDate, endDate, page);
        }

        public async Task<IActionResult> SecurityLogs(
            string? severity, string? search,
            DateTime? startDate, DateTime? endDate, int page = 1)
        {
            return await BuildAuditLogView("Security", severity, search, startDate, endDate, page);
        }

        private async Task<IActionResult> BuildAuditLogView(
            string logType, string? severity, string? search,
            DateTime? startDate, DateTime? endDate, int page)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!HasRole(Roles.SuperAdmin, Roles.Admin)) return AccessDenied();

            const int pageSize = 20;
            int? tenantId = GetTenantFilter();

            var allLogs = await _context.AuditLogs
                .Include(a => a.User)
                .Where(a => a.LogType == logType && (tenantId == null || a.TenantID == tenantId))
                .OrderByDescending(a => a.CreatedAt)
                .ToListAsync();

            // KPI counts
            int totalAll = allLogs.Count;
            int infoCount = allLogs.Count(a => a.Severity == "Info");
            int warningCount = allLogs.Count(a => a.Severity == "Warning");
            int criticalCount = allLogs.Count(a => a.Severity == "Critical");

            // Apply filters
            var query = allLogs.AsEnumerable();

            if (!string.IsNullOrEmpty(severity))
                query = query.Where(a => a.Severity == severity);

            if (startDate.HasValue)
                query = query.Where(a => a.CreatedAt >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(a => a.CreatedAt <= endDate.Value.AddDays(1));

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.ToLower();
                query = query.Where(a =>
                    (a.Action?.ToLower().Contains(s) == true) ||
                    (a.Details?.ToLower().Contains(s) == true) ||
                    (a.User?.FullName?.ToLower().Contains(s) == true)
                );
            }

            var filtered = query.ToList();
            int totalFiltered = filtered.Count;
            int totalPages = (int)Math.Ceiling(totalFiltered / (double)pageSize);
            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var pagedLogs = filtered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(a => new AuditLogItem
                {
                    AuditLogID = a.AuditLogID,
                    LogType = a.LogType,
                    Severity = a.Severity,
                    Action = a.Action,
                    Details = a.Details,
                    IPAddress = a.IPAddress,
                    CreatedAt = a.CreatedAt,
                    UserName = a.User?.FullName ?? "System"
                }).ToList();

            var vm = new AuditLogViewModel
            {
                TotalLogs = totalAll,
                SystemLogs = infoCount,
                SecurityLogs = warningCount,
                CriticalLogs = criticalCount,
                Logs = pagedLogs,
                LogTypeFilter = logType,
                SeverityFilter = severity,
                SearchQuery = search,
                StartDate = startDate,
                EndDate = endDate,
                CurrentPage = page,
                TotalPages = totalPages,
                PageSize = pageSize
            };

            return View(logType == "System" ? "SystemLogs" : "SecurityLogs", vm);
        }

        [HttpGet]
        public async Task<IActionResult> GetLogDetails(int id)
        {
            if (!IsAuthenticated) return Unauthorized();
            if (!HasRole(Roles.SuperAdmin, Roles.Admin)) return Forbid();

            int? tenantId = GetTenantFilter();

            var log = await _context.AuditLogs
                .Include(a => a.User)
                .FirstOrDefaultAsync(a => a.AuditLogID == id && (tenantId == null || a.TenantID == tenantId));

            if (log == null)
                return Json(new { success = false });

            return Json(new
            {
                success = true,
                data = new AuditLogDetailItem
                {
                    AuditLogID = log.AuditLogID,
                    LogType = log.LogType,
                    Severity = log.Severity,
                    Action = log.Action,
                    Details = log.Details,
                    IPAddress = log.IPAddress,
                    CreatedAt = log.CreatedAt,
                    UserName = log.User?.FullName ?? "System",
                    UserEmail = log.User?.Email ?? "—"
                }
            });
        }
    }
}
