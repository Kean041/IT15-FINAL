using FinSight.Data;
using FinSight.Models;
using FinSight.Helpers;
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
        // Static company total budget for display in analytics (Can be dynamic later)
        private static readonly decimal _masterCompanyBudget = 3500000.00m;

        public BudgetAllocationController(FinSightDbContext context)
        {
            _context = context;
        }

        // GET: BudgetAllocation
        public async Task<IActionResult> Index(string searchString, string periodFilter, int page = 1)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // All roles can view budgets
            int? tenantFilter = GetTenantFilter();

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

            // 4. Pagination execution
            int totalRecords = filteredResults.Count;
            int totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            if(totalPages == 0) totalPages = 1;

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
                new SelectListItem { Value = "Draft", Text = "Draft" },
                new SelectListItem { Value = "Active", Text = "Active" },
                new SelectListItem { Value = "Closed", Text = "Closed" }
            };

            // Pass RBAC flags to view for conditional UI
            ViewBag.CanWrite  = CanWriteFinancials;
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

             // RBAC: Only Super Admin, Admin, Finance Manager can create
             if (!CanWriteFinancials) return AccessDenied();

             int tenantId = IsSuperAdmin ? (CurrentTenantID ?? 0) : CurrentTenantID!.Value;
             int userId   = CurrentUserID!.Value;

             string trimmedDeptName = DepartmentName?.Trim() ?? "General";

             var department = await _context.Departments.FirstOrDefaultAsync(d => d.DepartmentName == trimmedDeptName && d.TenantID == tenantId);
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
                 Status = Status,
                 CreatedBy = userId,
                 CreatedAt = DateTime.Now,
                 UpdatedAt = DateTime.Now
             };

             _context.Budgets.Add(budget);
             await _context.SaveChangesAsync();
             return RedirectToAction(nameof(Index));
        }

        // POST: BudgetAllocation/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string DepartmentName, string Category, decimal Amount, int Year, string Status)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Super Admin, Admin, Finance Manager can edit
            if (!CanWriteFinancials) return AccessDenied();

            int tenantId = IsSuperAdmin ? (CurrentTenantID ?? 0) : CurrentTenantID!.Value;

            var existing = await _context.Budgets.FirstOrDefaultAsync(b => b.BudgetID == id && (IsSuperAdmin || b.TenantID == tenantId));
            
            if (existing != null)
            {
                string trimmedDeptName = DepartmentName?.Trim() ?? "General";
                var department = await _context.Departments.FirstOrDefaultAsync(d => d.DepartmentName == trimmedDeptName && d.TenantID == existing.TenantID);
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
            
            var existing = await _context.Budgets.FirstOrDefaultAsync(b => b.BudgetID == id && (tenantFilter == null || b.TenantID == tenantFilter.Value));
            if (existing != null)
            {
                _context.Budgets.Remove(existing);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}