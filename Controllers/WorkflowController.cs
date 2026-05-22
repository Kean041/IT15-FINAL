using FinSight.Data;
using FinSight.Models;
using FinSight.Models.ViewModels;
using FinSight.Helpers;
using FinSight.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace FinSight.Controllers
{
    public class WorkflowController : BaseController
    {
        private readonly FinSightDbContext _context;
        private readonly AuditLogService _auditLog;
        private readonly NotificationService _notification;

        public WorkflowController(FinSightDbContext context, AuditLogService auditLog, NotificationService notification)
        {
            _context = context;
            _auditLog = auditLog;
            _notification = notification;
        }

        // ─────────────────────────────────────────────
        // GET: Workflow/Approval
        // All authenticated users can view the approval list
        // ─────────────────────────────────────────────
        public async Task<IActionResult> Approval(
            string searchString,
            string periodFilter,
            string statusFilter,
            int? departmentFilter,
            int page = 1)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            int? tenantFilter = GetTenantFilter();
            int pageSize = 10;

            var query = _context.BudgetRequests
                .Include(r => r.Department)
                .Include(r => r.Budget)
                .Include(r => r.Submitter)
                .Include(r => r.Approver)
                .AsQueryable();

            // ── Multi-Tenant Isolation ──
            if (tenantFilter != null)
            {
                query = query.Where(r => r.TenantID == tenantFilter.Value);
            }

            // ── Text Search (Department Name, Budget Category, Submitter Name) ──
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(r =>
                    (r.Department != null && r.Department.DepartmentName.Contains(searchString)) ||
                    (r.Budget != null && r.Budget.Category.Contains(searchString)) ||
                    (r.Submitter != null && r.Submitter.FullName.Contains(searchString)));
            }

            // ── Status Filter ──
            if (!string.IsNullOrEmpty(statusFilter))
            {
                query = query.Where(r => r.Status == statusFilter);
            }

            // ── Department Filter ──
            if (departmentFilter.HasValue)
            {
                query = query.Where(r => r.DepartmentID == departmentFilter.Value);
            }

            // ── Date Period Filter ──
            if (!string.IsNullOrEmpty(periodFilter))
            {
                DateTime now = DateTime.Now;
                query = periodFilter switch
                {
                    "Day" => query.Where(r => r.CreatedAt.Date == now.Date),
                    "Week" => query.Where(r => r.CreatedAt >= now.AddDays(-7)),
                    "Month" => query.Where(r => r.CreatedAt >= now.AddMonths(-1)),
                    _ => query
                };
            }

            // ── Execute & Order ──
            var orderedQuery = query.OrderByDescending(r => r.CreatedAt);
            var allResults = await orderedQuery.ToListAsync();

            // ── Analytics Cards ──
            ViewBag.TotalRequests = allResults.Count;
            ViewBag.PendingCount = allResults.Count(r => r.Status == "Pending");
            ViewBag.ApprovedCount = allResults.Count(r => r.Status == "Approved");
            ViewBag.RejectedCount = allResults.Count(r => r.Status == "Rejected");

            // ── Map to ViewModel ──
            var viewModels = allResults.Select(r => new BudgetRequestViewModel
            {
                RequestID = r.RequestID,
                DepartmentName = r.Department?.DepartmentName ?? "N/A",
                DepartmentID = r.DepartmentID,
                RequestedAmount = r.RequestedAmount,
                BudgetCategory = r.Budget?.Category ?? "N/A",
                BudgetID = r.BudgetID,
                Title = r.Title,
                Description = r.Description,
                DateNeeded = r.DateNeeded,
                Status = r.Status,
                SubmittedByName = r.Submitter?.FullName ?? "Unknown",
                SubmittedDate = r.CreatedAt,
                ApprovedByName = r.Approver?.FullName,
                ApprovedDate = r.ApprovedDate,
                RejectionReason = r.RejectionReason,
                UpdatedByName = null, // Could resolve via lookup if needed
                UpdatedAt = r.UpdatedAt
            }).ToList();

            // ── Pagination ──
            int totalPages = (int)Math.Ceiling(viewModels.Count / (double)pageSize);
            if (totalPages == 0) totalPages = 1;
            var pagedData = viewModels.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // ── Filter Dropdowns ──
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

            // Budget dropdown for the submit modal (filtered by tenant)
            var budgetQuery = _context.Budgets.Include(b => b.Department).AsQueryable();
            if (tenantFilter != null)
                budgetQuery = budgetQuery.Where(b => b.TenantID == tenantFilter.Value);

            ViewBag.Budgets = await budgetQuery
                .OrderBy(b => b.Category)
                .Select(b => new SelectListItem
                {
                    Value = b.BudgetID.ToString(),
                    Text = b.Category + " — " + (b.Department != null ? b.Department.DepartmentName : "N/A") + " ($" + b.Amount.ToString("N2") + ")"
                }).ToListAsync();

            ViewBag.Statuses = new List<SelectListItem>
            {
                new SelectListItem { Value = "", Text = "All Statuses" },
                new SelectListItem { Value = "Pending", Text = "Pending" },
                new SelectListItem { Value = "Approved", Text = "Approved" },
                new SelectListItem { Value = "Rejected", Text = "Rejected" }
            };

            // ── Carry Over Filters ──
            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentPeriod = periodFilter;
            ViewBag.CurrentStatus = statusFilter;
            ViewBag.CurrentDepartment = departmentFilter;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            // ── RBAC Flags for View ──
            ViewBag.CanApprove = CanApproveRequests;
            ViewBag.CanSubmit = CanSubmitBudgetRequests;
            ViewBag.RoleID = CurrentRoleID;

            return View(pagedData);
        }

        // ─────────────────────────────────────────────
        // POST: Workflow/SubmitRequest
        // Only Department Heads can submit budget requests
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitRequest(int BudgetID, string Title, string? Description, decimal RequestedAmount, DateTime DateNeeded)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Department Head can submit
            if (!CanSubmitBudgetRequests) return AccessDenied();

            // Validate inputs
            if (RequestedAmount <= 0)
            {
                TempData["Error"] = "Requested amount must be greater than zero.";
                return RedirectToAction(nameof(Approval));
            }

            if (string.IsNullOrWhiteSpace(Title))
            {
                TempData["Error"] = "Request title is required.";
                return RedirectToAction(nameof(Approval));
            }

            // Resolve the Budget to get DepartmentID
            int tenantId = CurrentTenantID!.Value;
            var budget = await _context.Budgets
                .FirstOrDefaultAsync(b => b.BudgetID == BudgetID && b.TenantID == tenantId);

            if (budget == null)
            {
                TempData["Error"] = "Selected budget was not found.";
                return RedirectToAction(nameof(Approval));
            }

            // Calculate remaining budget
            var totalApproved = await _context.BudgetRequests
                .Where(r => r.BudgetID == BudgetID && r.Status == "Approved")
                .SumAsync(r => r.RequestedAmount);

            var remaining = budget.Amount - totalApproved;

            if (RequestedAmount > remaining)
            {
                TempData["Error"] = $"Requested amount (₱{RequestedAmount:N2}) exceeds the remaining allocated budget (₱{remaining:N2}).";
                return RedirectToAction(nameof(Approval));
            }

            var request = new BudgetRequest
            {
                Title = Title.Trim(),
                Description = Description?.Trim(),
                RequestedAmount = RequestedAmount,
                DateNeeded = DateNeeded,
                DepartmentID = budget.DepartmentID,
                TenantID = tenantId,
                BudgetID = BudgetID,
                SubmittedBy = CurrentUserID!.Value,
                Status = "Pending",
                CreatedAt = DateTime.Now
            };

            _context.BudgetRequests.Add(request);
            await _context.SaveChangesAsync();

            // Audit: Budget Submission
            await _auditLog.LogSystemAction(tenantId, CurrentUserID,
                "BudgetSubmitted", $"Budget request #{request.RequestID} for ${RequestedAmount:N2} submitted.",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            // Notify Executives/Admins
            await _notification.CreateNotificationAsync(tenantId, null, "Approval", 
                "New Budget Request", 
                $"A new budget request for ${RequestedAmount:N2} was submitted and requires approval.", 
                "/Workflow/Approval");

            TempData["Success"] = "Budget request submitted successfully.";
            return RedirectToAction(nameof(Approval));
        }

        // ─────────────────────────────────────────────
        // POST: Workflow/ApproveRequest
        // Executives / Admins can approve pending requests
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ApproveRequest(int id)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only approved roles can approve
            if (!CanApproveRequests) return AccessDenied();

            int? tenantFilter = GetTenantFilter();

            var request = await _context.BudgetRequests
                .FirstOrDefaultAsync(r => r.RequestID == id
                    && (tenantFilter == null || r.TenantID == tenantFilter.Value));

            if (request == null)
            {
                TempData["Error"] = "Request not found.";
                return RedirectToAction(nameof(Approval));
            }

            // Only Pending requests can be approved
            if (request.Status != "Pending")
            {
                TempData["Error"] = "Only pending requests can be approved.";
                return RedirectToAction(nameof(Approval));
            }

            request.Status = "Approved";
            request.ApprovedBy = CurrentUserID!.Value;
            request.ApprovedDate = DateTime.Now;
            request.UpdatedBy = CurrentUserID.Value;
            request.UpdatedAt = DateTime.Now;

            _context.BudgetRequests.Update(request);
            await _context.SaveChangesAsync();

            // Audit: Budget Approval
            await _auditLog.LogSystemAction(CurrentTenantID, CurrentUserID,
                "BudgetApproved", $"Budget request #{request.RequestID} approved.",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            // Notify Submitter
            await _notification.CreateNotificationAsync(request.TenantID, request.SubmittedBy, "Approval", 
                "Budget Approved", 
                $"Your budget request #{request.RequestID} has been approved.", 
                "/Workflow/Approval");

            TempData["Success"] = "Request approved successfully.";
            return RedirectToAction(nameof(Approval));
        }

        // ─────────────────────────────────────────────
        // POST: Workflow/RejectRequest
        // Executives / Admins can reject pending requests
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectRequest(int id, string? rejectionReason)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only approved roles can reject
            if (!CanApproveRequests) return AccessDenied();

            int? tenantFilter = GetTenantFilter();

            var request = await _context.BudgetRequests
                .FirstOrDefaultAsync(r => r.RequestID == id
                    && (tenantFilter == null || r.TenantID == tenantFilter.Value));

            if (request == null)
            {
                TempData["Error"] = "Request not found.";
                return RedirectToAction(nameof(Approval));
            }

            // Only Pending requests can be rejected
            if (request.Status != "Pending")
            {
                TempData["Error"] = "Only pending requests can be rejected.";
                return RedirectToAction(nameof(Approval));
            }

            request.Status = "Rejected";
            request.RejectionReason = rejectionReason;
            request.UpdatedBy = CurrentUserID!.Value;
            request.UpdatedAt = DateTime.Now;

            _context.BudgetRequests.Update(request);
            await _context.SaveChangesAsync();

            // Audit: Budget Rejection
            await _auditLog.LogSystemAction(CurrentTenantID, CurrentUserID,
                "BudgetRejected", $"Budget request #{request.RequestID} rejected. Reason: {rejectionReason ?? "No reason provided"}.",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            // Notify Submitter
            await _notification.CreateNotificationAsync(request.TenantID, request.SubmittedBy, "Approval", 
                "Budget Rejected", 
                $"Your budget request #{request.RequestID} was rejected. Reason: {rejectionReason ?? "N/A"}", 
                "/Workflow/Approval");

            TempData["Success"] = "Request rejected.";
            return RedirectToAction(nameof(Approval));
        }
    }
}
