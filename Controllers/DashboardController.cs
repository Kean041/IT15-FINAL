using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FinSight.Data;
using FinSight.Models.ViewModels;
using FinSight.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinSight.Controllers
{
    public class DashboardController : BaseController
    {
        private readonly FinSightDbContext _db;
        private readonly AlphaVantageService _alphaVantage;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(
            FinSightDbContext db,
            AlphaVantageService alphaVantage,
            ILogger<DashboardController> logger)
        {
            _db = db;
            _alphaVantage = alphaVantage;
            _logger = logger;
        }

        public async Task<IActionResult> Index()
        {
            if (!IsAuthenticated) return RedirectToLogin();

            ViewBag.RoleID = CurrentRoleID;
            ViewBag.RoleName = HttpContext.Session.GetString("RoleName") ?? "Admin";

            var tenantFilter = GetTenantFilter();

            try
            {
                var budgetsQuery = _db.Budgets.AsNoTracking().AsQueryable();
                if (tenantFilter != null) budgetsQuery = budgetsQuery.Where(b => b.TenantID == tenantFilter.Value);
                var totalBudget = await budgetsQuery.SumAsync(b => (decimal?)b.Amount) ?? 0;

                var expensesQuery = _db.Expenses.AsNoTracking().AsQueryable();
                if (tenantFilter != null) expensesQuery = expensesQuery.Where(e => e.TenantID == tenantFilter.Value);
                var totalExpenses = await expensesQuery.SumAsync(e => (decimal?)e.Amount) ?? 0;

                var requestsQuery = _db.BudgetRequests.AsNoTracking().AsQueryable();
                if (tenantFilter != null) requestsQuery = requestsQuery.Where(r => r.TenantID == tenantFilter.Value);
                var pendingRequests = await requestsQuery.CountAsync(r => r.Status == "Pending");

                var deptBudgets = await budgetsQuery
                    .GroupBy(b => b.Department != null ? b.Department.DepartmentName : "Unassigned")
                    .Select(g => new { Department = g.Key, Total = g.Sum(b => b.Amount) })
                    .OrderBy(x => x.Department)
                    .ToListAsync();

                var deptExpenses = await expensesQuery
                    .GroupBy(e => e.Department != null ? e.Department.DepartmentName : "Unassigned")
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

                var currentYear = DateTime.Now.Year;
                var monthlyExpenses = await expensesQuery
                    .Where(e => e.ExpenseDate.Year == currentYear)
                    .GroupBy(e => e.ExpenseDate.Month)
                    .Select(g => new { Month = g.Key, Total = g.Sum(e => e.Amount) })
                    .OrderBy(g => g.Month)
                    .ToListAsync();

                var lineLabels = GetMonthLabels();
                var lineData = Enumerable.Range(1, 12)
                    .Select(m => monthlyExpenses.FirstOrDefault(e => e.Month == m)?.Total ?? 0)
                    .ToList();

                var recentRequests = await requestsQuery
                    .Include(r => r.Department)
                    .Include(r => r.Submitter)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(8)
                    .Select(r => new RecentRequestItem
                    {
                        RequestID = r.RequestID,
                        DepartmentName = r.Department != null ? r.Department.DepartmentName : "Unassigned",
                        RequestedAmount = r.RequestedAmount,
                        Status = r.Status,
                        Date = r.CreatedAt,
                        SubmittedBy = r.Submitter != null ? r.Submitter.FullName : "Unknown"
                    })
                    .ToListAsync();

                var viewModel = new DashboardViewModel
                {
                    TotalBudget = totalBudget,
                    TotalExpenses = totalExpenses,
                    PendingRequests = pendingRequests,
                    BarChartLabels = allDepts,
                    BarChartBudgetData = barBudgetData,
                    BarChartExpenseData = barExpenseData,
                    LineChartLabels = lineLabels,
                    LineChartData = lineData,
                    RecentRequests = recentRequests,
                    UserName = CurrentFullName,
                    RoleName = HttpContext.Session.GetString("RoleName") ?? "Admin",
                    MarketData = await LoadMarketDataAsync()
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Dashboard data load failed for user {UserID}, role {RoleID}, tenant {TenantID}.",
                    CurrentUserID,
                    CurrentRoleID,
                    tenantFilter);

                return View(CreateFallbackDashboardViewModel());
            }
        }

        private async Task<MarketDataViewModel?> LoadMarketDataAsync()
        {
            var roleId = CurrentRoleID ?? 1;
            if (roleId != Helpers.Roles.SuperAdmin &&
                roleId != Helpers.Roles.Admin &&
                roleId != Helpers.Roles.FinanceManager &&
                roleId != Helpers.Roles.Executive)
            {
                return null;
            }

            try
            {
                return await _alphaVantage.GetAllExchangeRatesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Market data load failed on dashboard.");
                return new MarketDataViewModel
                {
                    HasError = true,
                    ErrorMessage = "Market data temporarily unavailable."
                };
            }
        }

        private DashboardViewModel CreateFallbackDashboardViewModel()
        {
            return new DashboardViewModel
            {
                LineChartLabels = GetMonthLabels(),
                LineChartData = Enumerable.Repeat(0m, 12).ToList(),
                UserName = CurrentFullName,
                RoleName = HttpContext.Session.GetString("RoleName") ?? "Admin",
                MarketData = new MarketDataViewModel
                {
                    HasError = true,
                    ErrorMessage = "Dashboard data temporarily unavailable."
                }
            };
        }

        private static List<string> GetMonthLabels()
        {
            return Enumerable.Range(1, 12)
                .Select(m => CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedMonthName(m))
                .ToList();
        }
    }
}
