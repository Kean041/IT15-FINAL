using FinSight.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FinSight.Controllers
{
    /// <summary>
    /// Abstract base controller providing reusable session-based
    /// authentication and role-based authorization helpers.
    /// All authenticated controllers should inherit from this.
    /// </summary>
    public abstract class BaseController : Controller
    {
        // ─────────────────────────────────────────────
        // Session Accessors
        // ─────────────────────────────────────────────

        /// <summary>Current user's ID from session, or null if not logged in.</summary>
        protected int? CurrentUserID  => HttpContext.Session.GetInt32("UserID");

        /// <summary>Current user's RoleID from session, or null if not logged in.</summary>
        protected int? CurrentRoleID  => HttpContext.Session.GetInt32("RoleID");

        /// <summary>Current user's TenantID from session, or null if not logged in.</summary>
        protected int? CurrentTenantID => HttpContext.Session.GetInt32("TenantID");

        /// <summary>Current user's full name from session.</summary>
        protected string CurrentFullName => HttpContext.Session.GetString("FullName") ?? "Unknown";

        // ─────────────────────────────────────────────
        // Role Checks
        // ─────────────────────────────────────────────

        protected bool IsSuperAdmin     => CurrentRoleID == Roles.SuperAdmin;
        protected bool IsAdmin          => CurrentRoleID == Roles.Admin;
        protected bool IsFinanceManager => CurrentRoleID == Roles.FinanceManager;
        protected bool IsDeptHead       => CurrentRoleID == Roles.DepartmentHead;
        protected bool IsExecutive      => CurrentRoleID == Roles.Executive;

        // ─────────────────────────────────────────────
        // Auth & Role Validation
        // ─────────────────────────────────────────────

        /// <summary>
        /// Returns true if the user is authenticated (has a valid session).
        /// </summary>
        protected bool IsAuthenticated =>
            CurrentUserID != null && CurrentRoleID != null && CurrentTenantID != null;

        /// <summary>
        /// Returns a redirect to the login page.
        /// Call this when session is missing.
        /// </summary>
        protected IActionResult RedirectToLogin()
        {
            return RedirectToAction("Login", "Auth");
        }

        /// <summary>
        /// Returns true if the current user's role is in the allowed list.
        /// </summary>
        protected bool HasRole(params int[] allowedRoles)
        {
            if (CurrentRoleID == null) return false;
            return allowedRoles.Contains(CurrentRoleID.Value);
        }

        /// <summary>
        /// Returns an Unauthorized result with a JSON message.
        /// </summary>
        protected IActionResult AccessDenied()
        {
            TempData["Error"] = "You do not have permission to perform this action.";
            return RedirectToAction("Index", "Dashboard");
        }

        // ─────────────────────────────────────────────
        // Tenant Filtering
        // ─────────────────────────────────────────────

        /// <summary>
        /// Returns the TenantID to filter data by.
        /// Returns null for Super Admin (sees all tenants).
        /// </summary>
        protected int? GetTenantFilter()
        {
            if (IsSuperAdmin) return null; // Super Admin sees everything
            return CurrentTenantID;
        }

        // ─────────────────────────────────────────────
        // Permission Helpers (financial modules)
        // ─────────────────────────────────────────────

        /// <summary>Can this role create/edit financial records?</summary>
        protected bool CanWriteFinancials =>
            CurrentRoleID != null && Roles.CanWriteFinancials(CurrentRoleID.Value);

        /// <summary>Can this role delete records?</summary>
        protected bool CanDeleteRecords =>
            CurrentRoleID != null && Roles.CanDelete(CurrentRoleID.Value);

        /// <summary>Can this role approve/reject workflow requests?</summary>
        protected bool CanApproveRequests =>
            CurrentRoleID != null && Roles.CanApprove(CurrentRoleID.Value);

        /// <summary>Can this role submit budget requests? (Department Head only)</summary>
        protected bool CanSubmitBudgetRequests =>
            CurrentRoleID != null && Roles.CanSubmitBudgetRequest(CurrentRoleID.Value);
    }
}
