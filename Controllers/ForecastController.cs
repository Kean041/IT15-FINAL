using FinSight.Data;
using FinSight.Models;
using FinSight.Helpers;
using FinSight.Services;
using FinSight.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FinSight.Controllers
{
    public class ForecastController : BaseController
    {
        private readonly FinSightDbContext _context;
        private readonly AlphaVantageService _alphaVantage;
        private readonly ILogger<ForecastController> _logger;

        public ForecastController(
            FinSightDbContext context,
            AlphaVantageService alphaVantage,
            ILogger<ForecastController> logger)
        {
            _context = context;
            _alphaVantage = alphaVantage;
            _logger = logger;
        }

        // GET: Forecast
        public async Task<IActionResult> Index(string searchString, int? yearFilter, int? departmentFilter, int page = 1)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Finance Manager, Admin, SuperAdmin can access forecasting
            if (!CanAccessForecasting) return AccessDenied();

            int? tenantFilter = GetTenantFilter();
            int pageSize = 10;

            try
            {
            // 1. Fetch Budgets
            var budgetQuery = _context.Budgets
                .AsNoTracking()
                .Include(b => b.Department)
                .AsQueryable();

            if (tenantFilter != null)
                budgetQuery = budgetQuery.Where(b => b.TenantID == tenantFilter.Value);

            if (yearFilter.HasValue)
                budgetQuery = budgetQuery.Where(b => b.Year == yearFilter.Value);

            if (departmentFilter.HasValue)
                budgetQuery = budgetQuery.Where(b => b.DepartmentID == departmentFilter.Value);

            var budgets = await budgetQuery.ToListAsync();

            // 2. Fetch Expenses grouped by BudgetID
            var expenseQuery = _context.Expenses.AsNoTracking().AsQueryable();

            if (tenantFilter != null)
                expenseQuery = expenseQuery.Where(e => e.TenantID == tenantFilter.Value);

            var expenseTotals = await expenseQuery
                .GroupBy(e => e.BudgetID)
                .Select(g => new
                {
                    BudgetID = g.Key,
                    TotalExpenses = g.Sum(e => e.Amount),
                    MinDate = g.Min(e => e.ExpenseDate),
                    MaxDate = g.Max(e => e.ExpenseDate)
                })
                .ToListAsync();

            var expenseDict = expenseTotals.ToDictionary(e => e.BudgetID);

            // 3. Fetch Alpha Vantage Inflation Rate
            decimal inflationRate = 0m;
            try
            {
                var economicData = await _alphaVantage.GetEconomicDataAsync();
                ViewBag.EconomicData = economicData;
                if (economicData != null && economicData.Inflation.LatestValue.HasValue)
                {
                    inflationRate = economicData.Inflation.LatestValue.Value / 100m;
                }
            }
            catch
            {
                ViewBag.EconomicData = null;
            }

            ViewBag.AppliedInflationRate = inflationRate;

            // 4. Compute Dynamic Forecasts
            var forecastResults = new List<DynamicForecastViewModel>();
            
            DateTime today = DateTime.Now;

            foreach (var b in budgets)
            {
                decimal totalExpenses = 0m;
                decimal runRate = 0m;
                decimal futureExpenses = 0m;

                if (expenseDict.TryGetValue(b.BudgetID, out var expData))
                {
                    totalExpenses = expData.TotalExpenses;

                    // Simple run rate calculation based on days elapsed in the year
                    DateTime startOfYear = new DateTime(b.Year, 1, 1);
                    DateTime currentDateToUse = (b.Year == today.Year) ? today : new DateTime(b.Year, 12, 31);
                    
                    int elapsedDays = (currentDateToUse - startOfYear).Days;
                    if (elapsedDays <= 0) elapsedDays = 1;

                    int totalDaysInYear = DateTime.IsLeapYear(b.Year) ? 366 : 365;

                    // Daily run rate based on actual expenses so far
                    decimal dailyRunRate = totalExpenses / elapsedDays;

                    // Projected total expenses for the entire year
                    runRate = dailyRunRate * totalDaysInYear;
                }

                // Adjust the run rate with inflation to get future predicted expenses
                futureExpenses = runRate * (1 + inflationRate);

                decimal currentUtilization = b.Amount > 0 ? (totalExpenses / b.Amount) * 100m : 0m;
                decimal futureUtilization = b.Amount > 0 ? (futureExpenses / b.Amount) * 100m : 0m;
                decimal projectedRemaining = b.Amount - futureExpenses;
                decimal predictedVariance = b.Amount - futureExpenses;

                string status;
                if (futureExpenses > b.Amount)
                    status = "Projected Over Budget";
                else if (futureExpenses < b.Amount)
                    status = "Projected Under Budget";
                else
                    status = "Projected On Track";

                forecastResults.Add(new DynamicForecastViewModel
                {
                    BudgetID = b.BudgetID,
                    DepartmentID = b.DepartmentID,
                    DepartmentName = b.Department?.DepartmentName ?? "Unknown",
                    Category = b.Category,
                    BudgetAmount = b.Amount,
                    CurrentExpenses = totalExpenses,
                    CurrentUtilization = currentUtilization,
                    RunRate = runRate,
                    AppliedInflationRate = inflationRate,
                    FutureExpenses = futureExpenses,
                    FutureBudgetUtilization = futureUtilization,
                    ProjectedRemainingBudget = projectedRemaining,
                    PredictedVariance = predictedVariance,
                    Status = status,
                    Year = b.Year
                });
            }

            // 5. Apply text search filter
            if (!string.IsNullOrEmpty(searchString))
            {
                forecastResults = forecastResults
                    .Where(f => f.DepartmentName.Contains(searchString, StringComparison.OrdinalIgnoreCase) ||
                                f.Category.Contains(searchString, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // 6. Analytics Summary
            ViewBag.TotalForecasts = forecastResults.Count;
            ViewBag.TotalProjectedExpenses = forecastResults.Sum(f => f.FutureExpenses);
            ViewBag.DeptsCovered = forecastResults.Select(f => f.DepartmentID).Distinct().Count();
            ViewBag.TotalProjectedVariance = forecastResults.Sum(f => f.PredictedVariance);

            // 7. Chart data — Forecast vs Actual comparison by department
            var chartDepts = forecastResults
                .Select(f => f.DepartmentName)
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var budgetSums = new List<decimal>();
            var forecastSums = new List<decimal>();

            foreach (var dept in chartDepts)
            {
                var deptItems = forecastResults.Where(f => f.DepartmentName == dept).ToList();
                budgetSums.Add(deptItems.Sum(f => f.BudgetAmount));
                forecastSums.Add(deptItems.Sum(f => f.FutureExpenses));
            }

            ViewBag.BarChartLabels = JsonSerializer.Serialize(chartDepts);
            ViewBag.BarChartBudget = JsonSerializer.Serialize(budgetSums);
            ViewBag.BarChartForecast = JsonSerializer.Serialize(forecastSums);

            // 8. Pagination
            var ordered = forecastResults.OrderBy(f => f.DepartmentName).ThenBy(f => f.Category).ToList();
            int totalPages = (int)Math.Ceiling(ordered.Count / (double)pageSize);
            if (totalPages == 0) totalPages = 1;

            var pagedData = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // 9. Filter dropdowns
            var deptDropdownQuery = _context.Departments.AsNoTracking().AsQueryable();
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
            var yearQuery = _context.Budgets.AsNoTracking().AsQueryable();
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

            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentYear = yearFilter;
            ViewBag.CurrentDepartment = departmentFilter;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            // Pass RBAC flags to view for conditional UI
            ViewBag.RoleID = CurrentRoleID;

            return View(pagedData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Forecast page failed for user {UserID}, role {RoleID}, tenant {TenantID}.",
                    CurrentUserID,
                    CurrentRoleID,
                    tenantFilter);

                PopulateFallbackViewBags(searchString, yearFilter, departmentFilter, page);
                return View(new List<DynamicForecastViewModel>());
            }
        }

        private void PopulateFallbackViewBags(string searchString, int? yearFilter, int? departmentFilter, int page)
        {
            ViewBag.EconomicData = null;
            ViewBag.AppliedInflationRate = 0m;
            ViewBag.TotalForecasts = 0;
            ViewBag.TotalProjectedExpenses = 0m;
            ViewBag.DeptsCovered = 0;
            ViewBag.TotalProjectedVariance = 0m;
            ViewBag.BarChartLabels = "[]";
            ViewBag.BarChartBudget = "[]";
            ViewBag.BarChartForecast = "[]";
            ViewBag.DepartmentList = new List<SelectListItem>();
            ViewBag.YearList = new List<SelectListItem>();
            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentYear = yearFilter;
            ViewBag.CurrentDepartment = departmentFilter;
            ViewBag.CurrentPage = page < 1 ? 1 : page;
            ViewBag.TotalPages = 1;
            ViewBag.RoleID = CurrentRoleID;
        }
    }
}
