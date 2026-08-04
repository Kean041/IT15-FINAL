using FinSight.Data;
using FinSight.Models;
using FinSight.Models.ViewModels;
using FinSight.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FinSight.Controllers
{
    public class VarianceController : BaseController
    {
        private readonly FinSightDbContext _context;

        public VarianceController(FinSightDbContext context)
        {
            _context = context;
        }

        // GET: Variance/Analysis
        public async Task<IActionResult> Analysis(string searchString, string periodFilter, int? yearFilter, int? departmentFilter, int page = 1)
        {
            // RBAC: Only Finance Manager, Admin, SuperAdmin can access analysis
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanAccessAnalysis) return AccessDenied();

            int? tenantFilter = GetTenantFilter();
            int pageSize = 10;

            // ──────────────────────────────────────────────
            // 1. Fetch all budgets (with Department navigation)
            // ──────────────────────────────────────────────
            var budgetQuery = _context.Budgets
                .Include(b => b.Department)
                .AsQueryable();

            if (tenantFilter != null)
                budgetQuery = budgetQuery.Where(b => b.TenantID == tenantFilter.Value);

            if (yearFilter.HasValue)
                budgetQuery = budgetQuery.Where(b => b.Year == yearFilter.Value);

            if (departmentFilter.HasValue)
                budgetQuery = budgetQuery.Where(b => b.DepartmentID == departmentFilter.Value);

            var budgets = await budgetQuery.ToListAsync();

            // ──────────────────────────────────────────────
            // 2. Fetch expenses grouped by BudgetID
            // ──────────────────────────────────────────────
            var expenseQuery = _context.Expenses.AsQueryable();

            if (tenantFilter != null)
                expenseQuery = expenseQuery.Where(e => e.TenantID == tenantFilter.Value);

            // Group expenses by BudgetID and sum amounts
            var expenseTotals = await expenseQuery
                .GroupBy(e => e.BudgetID)
                .Select(g => new
                {
                    BudgetID = g.Key,
                    TotalExpenses = g.Sum(e => e.Amount)
                })
                .ToListAsync();

            // Convert to dictionary for O(1) lookup
            var expenseDict = expenseTotals.ToDictionary(e => e.BudgetID, e => e.TotalExpenses);

            // ──────────────────────────────────────────────
            // 2.5 Fetch approved budget requests grouped by BudgetID
            // ──────────────────────────────────────────────
            var requestsQuery = _context.BudgetRequests.Where(r => r.Status == "Approved");
            
            if (tenantFilter != null)
                requestsQuery = requestsQuery.Where(r => r.TenantID == tenantFilter.Value);

            var requestTotals = await requestsQuery
                .GroupBy(r => r.BudgetID)
                .Select(g => new
                {
                    BudgetID = g.Key,
                    TotalApproved = g.Sum(r => r.RequestedAmount)
                })
                .ToListAsync();

            var requestDict = requestTotals.ToDictionary(r => r.BudgetID, r => r.TotalApproved);

            // ──────────────────────────────────────────────
            // 3. Compute variance for each budget line
            // ──────────────────────────────────────────────
            var varianceResults = budgets.Select(b =>
            {
                decimal totalExpenses = expenseDict.GetValueOrDefault(b.BudgetID, 0m);
                decimal totalApproved = requestDict.GetValueOrDefault(b.BudgetID, 0m);
                decimal variance = b.Amount - totalExpenses;
                decimal remainingBudget = b.Amount - totalExpenses;
                decimal utilization = b.Amount > 0 ? (totalExpenses / b.Amount) * 100 : 0m;

                string status;
                if (totalExpenses > b.Amount)
                    status = "Over Budget";
                else if (totalExpenses < b.Amount)
                    status = "Under Budget";
                else
                    status = "On Track";

                return new VarianceAnalysisViewModel
                {
                    BudgetID = b.BudgetID,
                    DepartmentID = b.DepartmentID,
                    DepartmentName = b.Department?.DepartmentName ?? "Unknown",
                    Category = b.Category,
                    BudgetAmount = b.Amount,
                    TotalApprovedRequests = totalApproved,
                    TotalExpenses = totalExpenses,
                    RemainingBudget = remainingBudget,
                    Variance = variance,
                    BudgetUtilizationPercentage = utilization,
                    Status = status,
                    Year = b.Year
                };
            }).ToList();

            // ──────────────────────────────────────────────
            // 4. Apply text search filter
            // ──────────────────────────────────────────────
            if (!string.IsNullOrEmpty(searchString))
            {
                varianceResults = varianceResults
                    .Where(v => v.DepartmentName.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                                v.Category.Contains(searchString, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // ──────────────────────────────────────────────
            // 5. Analytics summary for cards
            // ──────────────────────────────────────────────
            ViewBag.TotalRecords = varianceResults.Count;
            ViewBag.OverBudgetCount = varianceResults.Count(v => v.Status == "Over Budget");
            ViewBag.UnderBudgetCount = varianceResults.Count(v => v.Status == "Under Budget");
            ViewBag.OnTrackCount = varianceResults.Count(v => v.Status == "On Track");
            ViewBag.TotalVariance = varianceResults.Sum(v => v.Variance);

            // ──────────────────────────────────────────────
            // 6. Chart data — aggregated by department
            // ──────────────────────────────────────────────
            var chartDepts = varianceResults
                .Select(v => v.DepartmentName)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var budgetSums = new List<decimal>();
            var actualSums = new List<decimal>();

            foreach (var dept in chartDepts)
            {
                var deptItems = varianceResults.Where(v => v.DepartmentName == dept).ToList();
                budgetSums.Add(deptItems.Sum(v => v.BudgetAmount));
                actualSums.Add(deptItems.Sum(v => v.TotalExpenses));
            }

            ViewBag.ChartLabels = JsonSerializer.Serialize(chartDepts);
            ViewBag.ChartBudgets = JsonSerializer.Serialize(budgetSums);
            ViewBag.ChartActuals = JsonSerializer.Serialize(actualSums);

            // ──────────────────────────────────────────────
            // 7. Pagination
            // ──────────────────────────────────────────────
            var ordered = varianceResults.OrderBy(v => v.DepartmentName).ThenBy(v => v.Category).ToList();
            int totalPages = (int)Math.Ceiling(ordered.Count / (double)pageSize);
            if (totalPages == 0) totalPages = 1;

            var pagedData = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // ──────────────────────────────────────────────
            // 8. Filter dropdowns
            // ──────────────────────────────────────────────
            var deptDropdownQuery = _context.Departments.AsQueryable();
            if (tenantFilter != null)
                deptDropdownQuery = deptDropdownQuery.Where(d => d.TenantID == tenantFilter.Value);

            ViewBag.DepartmentList = await deptDropdownQuery
                .OrderBy(d => d.DepartmentName)
                .Select(d => new SelectListItem
                {
                    Value = d.DepartmentID.ToString(),
                    Text = d.DepartmentName
                }).ToListAsync();

            // Distinct years from budgets for year filter
            var yearQuery = _context.Budgets.AsQueryable();
            if (tenantFilter != null)
                yearQuery = yearQuery.Where(b => b.TenantID == tenantFilter.Value);

            ViewBag.YearList = await yearQuery
                .Select(b => b.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .Select(y => new SelectListItem
                {
                    Value = y.ToString(),
                    Text = y.ToString()
                }).ToListAsync();

            // Carry over filters
            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentPeriod = periodFilter;
            ViewBag.CurrentYear = yearFilter;
            ViewBag.CurrentDepartment = departmentFilter;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            // Pass RBAC info to view
            ViewBag.RoleID = CurrentRoleID;

            return View(pagedData);
        }
    }
}
