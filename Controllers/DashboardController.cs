using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FinSight.Data;
using FinSight.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FinSight.Controllers
{
    public class DashboardController : BaseController
    {
        private readonly FinSightDbContext _db;

        public DashboardController(FinSightDbContext db)
        {
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // Pass role info to view for conditional UI
            ViewBag.RoleID   = CurrentRoleID;
            ViewBag.RoleName = HttpContext.Session.GetString("RoleName") ?? "Admin";

            var tenantFilter = GetTenantFilter();

            // ── KPI: Total Budget ──────────────────────
            var budgetsQuery = _db.Budgets.AsQueryable();
            if (tenantFilter != null) budgetsQuery = budgetsQuery.Where(b => b.TenantID == tenantFilter);
            var totalBudget = await budgetsQuery.SumAsync(b => (decimal?)b.Amount) ?? 0;

            // ── KPI: Total Expenses ────────────────────
            var expensesQuery = _db.Expenses.AsQueryable();
            if (tenantFilter != null) expensesQuery = expensesQuery.Where(e => e.TenantID == tenantFilter);
            var totalExpenses = await expensesQuery.SumAsync(e => (decimal?)e.Amount) ?? 0;

            // ── KPI: Pending Requests ──────────────────
            var requestsQuery = _db.BudgetRequests.AsQueryable();
            if (tenantFilter != null) requestsQuery = requestsQuery.Where(r => r.TenantID == tenantFilter);
            var pendingRequests = await requestsQuery.CountAsync(r => r.Status == "Pending");

            // ── Bar Chart: Budget vs Expenses by Department ──
            var deptBudgets = await budgetsQuery
                .Include(b => b.Department)
                .GroupBy(b => b.Department!.DepartmentName)
                .Select(g => new { Department = g.Key, Total = g.Sum(b => b.Amount) })
                .OrderBy(x => x.Department)
                .ToListAsync();

            var deptExpenses = await expensesQuery
                .Include(e => e.Department)
                .GroupBy(e => e.Department!.DepartmentName)
                .Select(g => new { Department = g.Key, Total = g.Sum(e => e.Amount) })
                .ToListAsync();

            var allDepts = deptBudgets.Select(d => d.Department)
                .Union(deptExpenses.Select(d => d.Department))
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var barBudgetData = allDepts.Select(d =>
                deptBudgets.FirstOrDefault(b => b.Department == d)?.Total ?? 0).ToList();
            var barExpenseData = allDepts.Select(d =>
                deptExpenses.FirstOrDefault(e => e.Department == d)?.Total ?? 0).ToList();

            // ── Line Chart: Monthly Expenses Trend (current year) ──
            var currentYear = DateTime.Now.Year;
            var monthlyExpenses = await expensesQuery
                .Where(e => e.CreatedAt.Year == currentYear)
                .GroupBy(e => e.CreatedAt.Month)
                .Select(g => new { Month = g.Key, Total = g.Sum(e => e.Amount) })
                .OrderBy(g => g.Month)
                .ToListAsync();

            var lineLabels = Enumerable.Range(1, 12)
                .Select(m => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(m))
                .ToList();
            var lineData = Enumerable.Range(1, 12)
                .Select(m => monthlyExpenses.FirstOrDefault(e => e.Month == m)?.Total ?? 0)
                .ToList();

            // ── Recent Budget Requests ─────────────────
            var recentRequests = await requestsQuery
                .Include(r => r.Department)
                .Include(r => r.Submitter)
                .OrderByDescending(r => r.CreatedAt)
                .Take(8)
                .Select(r => new RecentRequestItem
                {
                    RequestID      = r.RequestID,
                    DepartmentName = r.Department!.DepartmentName,
                    RequestedAmount = r.RequestedAmount,
                    Status         = r.Status,
                    Date           = r.CreatedAt,
                    SubmittedBy    = r.Submitter != null
                        ? r.Submitter.FullName
                        : "Unknown"
                })
                .ToListAsync();

            var viewModel = new DashboardViewModel
            {
                TotalBudget      = totalBudget,
                TotalExpenses    = totalExpenses,
                PendingRequests  = pendingRequests,
                BarChartLabels   = allDepts,
                BarChartBudgetData  = barBudgetData,
                BarChartExpenseData = barExpenseData,
                LineChartLabels  = lineLabels,
                LineChartData    = lineData,
                RecentRequests   = recentRequests,
                UserName         = CurrentFullName,
                RoleName         = HttpContext.Session.GetString("RoleName") ?? "Admin"
            };

            return View(viewModel);
        }
    }
}
