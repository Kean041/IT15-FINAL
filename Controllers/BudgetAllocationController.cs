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
using Microsoft.AspNetCore.Http;

namespace FinSight.Controllers
{
    public class BudgetAllocationController : BaseController
    {
        private readonly FinSightDbContext _context;
        private readonly AuditLogService _auditLog;
        private readonly NotificationService _notification;

        // Static company total budget for display in analytics (Can be dynamic later)
        private static readonly decimal _masterCompanyBudget = 3500000.00m;

        public BudgetAllocationController(FinSightDbContext context, AuditLogService auditLog, NotificationService notification)
        {
            _context = context;
            _auditLog = auditLog;
            _notification = notification;
        }

        // GET: BudgetAllocation
        public async Task<IActionResult> Index(string searchString, string periodFilter, int page = 1)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            int? tenantFilter = GetTenantFilter();
            int? departmentHeadDepartmentId = IsDeptHead
                ? await GetCurrentDepartmentHeadDepartmentIdAsync()
                : null;

            int pageSize = 10;
            var query = _context.Budgets
                .Include(b => b.Department)
                .Include(b => b.Creator)
                .AsQueryable();

            // Apply tenant filter (Super Admin sees all)
            if (tenantFilter != null)
            {
                query = query.Where(b => b.TenantID == tenantFilter.Value);
            }

            // ── Department Head: restrict to their own department only ──
            if (IsDeptHead && departmentHeadDepartmentId.HasValue)
            {
                query = query.Where(b => b.DepartmentID == departmentHeadDepartmentId.Value);
            }
            else if (IsDeptHead)
            {
                query = query.Where(b => false);
                ViewBag.DepartmentAssignmentMissing = true;
            }

            // 1. Text Search filtering
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(b => (b.Department != null && b.Department.DepartmentName.Contains(searchString)) || 
                                         b.Category.Contains(searchString));
            }

            // 2. Date dropdown filtering logic (Day, Week, Month)
            if (!string.IsNullOrEmpty(periodFilter))
            {
                DateTime now = DateTime.Now;
                if (periodFilter == "Day")
                {
                    query = query.Where(b => b.CreatedAt.Date == now.Date);
                }
                else if (periodFilter == "Week")
                {
                    DateTime weekAgo = now.AddDays(-7);
                    query = query.Where(b => b.CreatedAt >= weekAgo);
                }
                else if (periodFilter == "Month")
                {
                    DateTime monthAgo = now.AddMonths(-1);
                    query = query.Where(b => b.CreatedAt >= monthAgo);
                }
            }

            var orderedQuery = query.OrderByDescending(b => b.CreatedAt);
            
            // Execute the query once fully resolving to List
            var filteredResults = await orderedQuery.ToListAsync();

            // 3. Prepare Analytics Summary Variables
            ViewBag.TotalAllocated = filteredResults.Sum(b => b.Amount);
            ViewBag.TotalDepartmentsCount = filteredResults.Select(b => b.DepartmentID).Distinct().Count();
            ViewBag.MasterBudget = _masterCompanyBudget;

            // ── Calculate remaining budget per allocation ──
            // Sum all APPROVED request amounts grouped by BudgetID
            var budgetIds = filteredResults.Select(b => b.BudgetID).ToList();
            var approvedAmounts = await _context.BudgetRequests
                .Where(r => budgetIds.Contains(r.BudgetID) && r.Status == "Approved")
                .GroupBy(r => r.BudgetID)
                .Select(g => new { BudgetID = g.Key, TotalApproved = g.Sum(r => r.RequestedAmount) })
                .ToDictionaryAsync(g => g.BudgetID, g => g.TotalApproved);

            ViewBag.ApprovedAmounts = approvedAmounts;

            // ── Department Head: load their request history ──
            if (IsDeptHead && departmentHeadDepartmentId.HasValue)
            {
                int deptId      = departmentHeadDepartmentId.Value;
                int deptTenant  = tenantFilter ?? 0;
                int deptUserId  = CurrentUserID!.Value;

                var deptRequests = await _context.BudgetRequests
                    .Include(r => r.Budget)
                    .Include(r => r.Approver)
                    .Where(r => r.DepartmentID == deptId
                             && r.TenantID     == deptTenant
                             && r.SubmittedBy  == deptUserId)
                    .OrderByDescending(r => r.CreatedAt)
                    .Select(r => new BudgetRequestViewModel
                    {
                        RequestID = r.RequestID,
                        DepartmentName = r.Department != null ? r.Department.DepartmentName : "N/A",
                        DepartmentID = r.DepartmentID,
                        RequestedAmount = r.RequestedAmount,
                        BudgetCategory = r.Budget != null ? r.Budget.Category : "N/A",
                        BudgetID = r.BudgetID,
                        Title = r.Title,
                        Description = r.Description,
                        DateNeeded = r.DateNeeded,
                        Status = r.Status,
                        SubmittedByName = r.Submitter != null ? r.Submitter.FullName : "Unknown",
                        SubmittedDate = r.CreatedAt,
                        ApprovedByName = r.Approver != null ? r.Approver.FullName : null,
                        ApprovedDate = r.ApprovedDate,
                        RejectionReason = r.RejectionReason
                    })
                    .ToListAsync();

                ViewBag.DepartmentRequests = deptRequests;
            }

            // 4. Pagination execution
            int totalRecords = filteredResults.Count;
            int totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            if(totalPages == 0) totalPages = 1;
            if(page < 1) page = 1;

            var pagedData = filteredResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Carry over filters
            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentPeriod = periodFilter;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            // Prepare dynamic dropdown for Departments
            var deptQuery = _context.Departments.AsQueryable();
            if (tenantFilter != null)
                deptQuery = deptQuery.Where(d => d.TenantID == tenantFilter.Value);

            var departments = await deptQuery
                .Select(d => new SelectListItem
                {
                    Value = d.DepartmentID.ToString(),
                    Text = d.DepartmentName
                }).ToListAsync();
                
            ViewBag.Departments = departments;

            ViewBag.Statuses = new List<SelectListItem>
            {
                new SelectListItem { Value = "Active", Text = "Active", Selected = true },
                new SelectListItem { Value = "Draft", Text = "Draft" },
                new SelectListItem { Value = "Closed", Text = "Closed" }
            };

            // ── Budget dropdown for Department Head submit request modal ──
            if (IsDeptHead && departmentHeadDepartmentId.HasValue)
            {
                int dropDeptId = departmentHeadDepartmentId.Value;

                var deptBudgets = await _context.Budgets
                    .Include(b => b.Department)
                    .Where(b => b.DepartmentID == dropDeptId
                             && (tenantFilter == null || b.TenantID == tenantFilter.Value)
                             && b.Status == "Active")
                    .OrderBy(b => b.Category)
                    .Select(b => new SelectListItem
                    {
                        Value = b.BudgetID.ToString(),
                        Text = b.Category + " — " + (b.Department != null ? b.Department.DepartmentName : "N/A") + " (₱" + b.Amount.ToString("N2") + ")"
                    }).ToListAsync();

                ViewBag.DeptBudgets = deptBudgets;
            }

            // Pass RBAC flags to view for conditional UI
            ViewBag.CanManage = CanManageAllocations;
            ViewBag.CanSubmit = CanSubmitBudgetRequests;
            ViewBag.CanDelete = CanDeleteRecords;
            ViewBag.RoleID    = CurrentRoleID;

            return View(pagedData);
        }

        // POST: BudgetAllocation/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string DepartmentName, string Category, decimal Amount, int Year, string Status)
        {
             if (!IsAuthenticated) return RedirectToLogin();

             // RBAC: Only Super Admin and Admin can create allocations
             if (!CanManageAllocations) return AccessDenied();

             int tenantId = IsSuperAdmin ? (CurrentTenantID ?? 0) : CurrentTenantID!.Value;
             int userId   = CurrentUserID!.Value;

             string trimmedDeptName = DepartmentName?.Trim() ?? "General";
             string allocationStatus = string.IsNullOrWhiteSpace(Status) ? "Active" : Status;

             var normalizedDeptName = trimmedDeptName.ToLower();
             var department = await _context.Departments.FirstOrDefaultAsync(d =>
                 d.TenantID == tenantId &&
                 d.DepartmentName.ToLower() == normalizedDeptName);
             if (department == null)
             {
                 department = new Department { DepartmentName = trimmedDeptName, TenantID = tenantId };
                 _context.Departments.Add(department);
                 await _context.SaveChangesAsync();
             }

             var budget = new Budget
             {
                 DepartmentID = department.DepartmentID,
                 TenantID = tenantId,
                 Category = Category,
                 Amount = Amount,
                 Year = Year,
                 Status = allocationStatus,
                 CreatedBy = userId,
                 CreatedAt = DateTime.Now,
                 UpdatedAt = DateTime.Now
             };

             _context.Budgets.Add(budget);
             await _context.SaveChangesAsync();

             // Audit: Allocation Created
             await _auditLog.LogSystemAction(tenantId, userId,
                 "AllocationCreated", $"Budget allocation '{Category}' of ₱{Amount:N2} created for {trimmedDeptName}.",
                 HttpContext.Connection.RemoteIpAddress?.ToString());

             // Notify tenant admins about new allocation
             await _notification.CreateTenantBroadcastAsync(tenantId, "System",
                 "Budget Allocation Created",
                 $"A new budget allocation '{Category}' of ₱{Amount:N2} was created for {trimmedDeptName}.",
                 "/BudgetAllocation");

             TempData["Success"] = "Budget allocation created successfully.";
             return RedirectToAction(nameof(Index));
        }

        // POST: BudgetAllocation/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string DepartmentName, string Category, decimal Amount, int Year, string Status)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Super Admin and Admin can edit allocations
            if (!CanManageAllocations) return AccessDenied();

            int tenantId = IsSuperAdmin ? (CurrentTenantID ?? 0) : CurrentTenantID!.Value;

            var existing = await _context.Budgets.FirstOrDefaultAsync(b => b.BudgetID == id && (IsSuperAdmin || b.TenantID == tenantId));
            
            if (existing != null)
            {
                string trimmedDeptName = DepartmentName?.Trim() ?? "General";
                var normalizedDeptName = trimmedDeptName.ToLower();
                var department = await _context.Departments.FirstOrDefaultAsync(d =>
                    d.TenantID == existing.TenantID &&
                    d.DepartmentName.ToLower() == normalizedDeptName);
                if (department == null)
                {
                    department = new Department { DepartmentName = trimmedDeptName, TenantID = existing.TenantID };
                    _context.Departments.Add(department);
                    await _context.SaveChangesAsync();
                }

                existing.DepartmentID = department.DepartmentID;
                existing.Category = Category;
                existing.Amount = Amount;
                existing.Year = Year;
                existing.Status = Status;
                existing.UpdatedAt = DateTime.Now;

                _context.Budgets.Update(existing);
                await _context.SaveChangesAsync();

                // Audit: Allocation Updated
                await _auditLog.LogSystemAction(CurrentTenantID, CurrentUserID,
                    "AllocationUpdated", $"Budget allocation #{id} '{Category}' updated to ₱{Amount:N2} for {trimmedDeptName}.",
                    HttpContext.Connection.RemoteIpAddress?.ToString());
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: BudgetAllocation/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Super Admin and Admin can delete
            if (!CanDeleteRecords) return AccessDenied();

            int? tenantFilter = GetTenantFilter();
            
            var existing = await _context.Budgets
                .Include(b => b.Department)
                .FirstOrDefaultAsync(b => b.BudgetID == id && (tenantFilter == null || b.TenantID == tenantFilter.Value));

            if (existing == null)
            {
                TempData["Error"] = "Budget allocation not found.";
                return RedirectToAction(nameof(Index));
            }

            await using var tx = await _context.Database.BeginTransactionAsync();
            try
            {
                // Remove all child records that reference this BudgetID
                var relatedForecasts = await _context.Forecasts.Where(f => f.BudgetID == id).ToListAsync();
                if (relatedForecasts.Any()) _context.Forecasts.RemoveRange(relatedForecasts);

                var relatedExpenses = await _context.Expenses.Where(e => e.BudgetID == id).ToListAsync();
                if (relatedExpenses.Any()) _context.Expenses.RemoveRange(relatedExpenses);

                var relatedRequests = await _context.BudgetRequests.Where(r => r.BudgetID == id).ToListAsync();
                if (relatedRequests.Any()) _context.BudgetRequests.RemoveRange(relatedRequests);

                var relatedScenarioDetails = await _context.ScenarioDetails.Where(sd => sd.BudgetID == id).ToListAsync();
                if (relatedScenarioDetails.Any()) _context.ScenarioDetails.RemoveRange(relatedScenarioDetails);

                _context.Budgets.Remove(existing);
                await _context.SaveChangesAsync();
                await tx.CommitAsync();

                string budgetLabel = $"{existing.Department?.DepartmentName} - {existing.Category} (₱{existing.Amount:N2})";

                // Audit: Allocation Deleted
                await _auditLog.LogSystemAction(CurrentTenantID, CurrentUserID,
                    "AllocationDeleted",
                    $"Budget allocation #{id} '{budgetLabel}' deleted along with {relatedForecasts.Count} forecast(s), {relatedExpenses.Count} expense(s), {relatedRequests.Count} request(s), {relatedScenarioDetails.Count} scenario detail(s).",
                    HttpContext.Connection.RemoteIpAddress?.ToString());

                TempData["Success"] = "Budget allocation and all related records deleted successfully.";
            }
            catch (Exception ex)
            {
                await tx.RollbackAsync();
                TempData["Error"] = $"Failed to delete budget allocation: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────
        // POST: BudgetAllocation/SubmitRequest
        // Department Heads can submit budget requests from the allocation page
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
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(Title))
            {
                TempData["Error"] = "Request title is required.";
                return RedirectToAction(nameof(Index));
            }

            // Resolve the Budget to get DepartmentID and validate remaining budget
            int tenantId = CurrentTenantID!.Value;
            int? departmentHeadDepartmentId = await GetCurrentDepartmentHeadDepartmentIdAsync();
            if (!departmentHeadDepartmentId.HasValue)
            {
                TempData["Error"] = "Your account is not assigned to a department. Please contact your administrator.";
                return RedirectToAction(nameof(Index));
            }

            var budget = await _context.Budgets
                .FirstOrDefaultAsync(b =>
                    b.BudgetID == BudgetID &&
                    b.TenantID == tenantId &&
                    b.DepartmentID == departmentHeadDepartmentId.Value &&
                    b.Status == "Active");

            if (budget == null)
            {
                TempData["Error"] = "Selected active budget allocation was not found for your department.";
                return RedirectToAction(nameof(Index));
            }

            // Calculate remaining budget
            var totalApproved = await _context.BudgetRequests
                .Where(r => r.BudgetID == BudgetID && r.Status == "Approved")
                .SumAsync(r => r.RequestedAmount);

            var remaining = budget.Amount - totalApproved;

            if (RequestedAmount > remaining)
            {
                TempData["Error"] = $"Requested amount (₱{RequestedAmount:N2}) exceeds the remaining allocated budget (₱{remaining:N2}).";
                return RedirectToAction(nameof(Index));
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
                "BudgetSubmitted", $"Budget request '{Title}' #{request.RequestID} for ₱{RequestedAmount:N2} submitted.",
                HttpContext.Connection.RemoteIpAddress?.ToString());

            // Notify Executives and Admins
            var approverRoleIds = new int?[] { 1, 4 }; // Admin and Executive
            var approvers = await _context.Users
                .Where(u => u.TenantID == tenantId && approverRoleIds.Contains(u.RoleID) && !u.IsArchived)
                .Select(u => u.UserID)
                .ToListAsync();

            foreach (var approverId in approvers)
            {
                await _notification.CreateNotificationAsync(tenantId, approverId, "Approval", 
                    "New Budget Request", 
                    $"A new budget request '{Title}' for ₱{RequestedAmount:N2} was submitted and requires approval.", 
                    "/Workflow/Approval");
            }

            TempData["Success"] = "Budget request submitted successfully.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<int?> GetCurrentDepartmentHeadDepartmentIdAsync()
        {
            if (!IsDeptHead || CurrentUserID == null)
                return null;

            if (CurrentDepartmentID.HasValue)
                return CurrentDepartmentID.Value;

            return await _context.Users
                .Where(u => u.UserID == CurrentUserID.Value && !u.IsArchived)
                .Select(u => u.DepartmentID)
                .FirstOrDefaultAsync();
        }
    }
}
