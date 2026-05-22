using FinSight.Data;
using FinSight.Models;
using FinSight.Helpers;
using FinSight.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace FinSight.Controllers
{
    public class ForecastController : BaseController
    {
        private readonly FinSightDbContext _context;
        private readonly AlphaVantageService _alphaVantage;

        public ForecastController(FinSightDbContext context, AlphaVantageService alphaVantage)
        {
            _context = context;
            _alphaVantage = alphaVantage;
        }

        // GET: Forecast
        public async Task<IActionResult> Index(string searchString, string periodFilter, int page = 1)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Finance Manager, Admin, SuperAdmin can access forecasting
            if (!CanAccessForecasting) return AccessDenied();

            int? tenantFilter = GetTenantFilter();

            int pageSize = 10;
            var query = _context.Forecasts
                .Include(f => f.Department)
                .Include(f => f.Budget)
                .Include(f => f.Creator)
                .AsQueryable();

            // Apply tenant filter (Super Admin sees all)
            if (tenantFilter != null)
            {
                query = query.Where(f => f.TenantID == tenantFilter.Value);
            }

            // 1. Text Search filtering
            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(f =>
                    (f.Department != null && f.Department.DepartmentName.Contains(searchString)) ||
                    (f.Budget != null && f.Budget.Category.Contains(searchString)) ||
                    f.ForecastType.Contains(searchString));
            }

            // 2. Date dropdown filtering (Day, Week, Month)
            if (!string.IsNullOrEmpty(periodFilter))
            {
                DateTime now = DateTime.Now;
                query = periodFilter switch
                {
                    "Day" => query.Where(f => f.CreatedAt.Date == now.Date),
                    "Week" => query.Where(f => f.CreatedAt >= now.AddDays(-7)),
                    "Month" => query.Where(f => f.CreatedAt >= now.AddMonths(-1)),
                    _ => query
                };
            }

            var orderedQuery = query.OrderByDescending(f => f.CreatedAt);
            var filteredResults = await orderedQuery.ToListAsync();

            // 3. Analytics Summary
            ViewBag.TotalForecasts = filteredResults.Count;
            ViewBag.AvgPredicted = filteredResults.Any() ? filteredResults.Average(f => f.PredictedAmount) : 0m;
            ViewBag.DeptsCovered = filteredResults.Select(f => f.DepartmentID).Distinct().Count();

            // 4. Chart data — Forecast vs Actual comparison by department
            var allForecastsQuery = _context.Forecasts.Include(f => f.Department).AsQueryable();
            var allBudgetsQuery   = _context.Budgets.Include(b => b.Department).AsQueryable();

            if (tenantFilter != null)
            {
                allForecastsQuery = allForecastsQuery.Where(f => f.TenantID == tenantFilter.Value);
                allBudgetsQuery   = allBudgetsQuery.Where(b => b.TenantID == tenantFilter.Value);
            }

            var allForecasts = await allForecastsQuery.ToListAsync();
            var allBudgets   = await allBudgetsQuery.ToListAsync();

            // Bar chart: per-department forecast vs actual (budget)
            var deptNames = allForecasts
                .Where(f => f.Department != null)
                .Select(f => f.Department!.DepartmentName)
                .Union(allBudgets.Where(b => b.Department != null).Select(b => b.Department!.DepartmentName))
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var forecastByDept = new List<decimal>();
            var actualByDept = new List<decimal>();
            var varianceByDept = new List<decimal>();

            foreach (var dept in deptNames)
            {
                var fSum = allForecasts
                    .Where(f => f.Department != null && f.Department.DepartmentName == dept)
                    .Sum(f => f.PredictedAmount);
                var aSum = allBudgets
                    .Where(b => b.Department != null && b.Department.DepartmentName == dept)
                    .Sum(b => b.Amount);
                forecastByDept.Add(fSum);
                actualByDept.Add(aSum);
                varianceByDept.Add(fSum - aSum);
            }

            ViewBag.BarChartLabels = JsonSerializer.Serialize(deptNames);
            ViewBag.BarChartForecast = JsonSerializer.Serialize(forecastByDept);
            ViewBag.BarChartActual = JsonSerializer.Serialize(actualByDept);

            // Line chart: monthly trend over last 6 months
            var lineLabels = new List<string>();
            var now2 = DateTime.Now;
            for (int m = 5; m >= 0; m--)
            {
                lineLabels.Add(now2.AddMonths(-m).ToString("MMM yyyy"));
            }

            var monthlyForecast = new List<decimal>();
            var monthlyActual = new List<decimal>();
            for (int m = 5; m >= 0; m--)
            {
                var target = now2.AddMonths(-m);
                var fSum = allForecasts
                    .Where(f => f.CreatedAt.Year == target.Year && f.CreatedAt.Month == target.Month)
                    .Sum(f => f.PredictedAmount);
                var aSum = allBudgets
                    .Where(b => b.CreatedAt.Year == target.Year && b.CreatedAt.Month == target.Month)
                    .Sum(b => b.Amount);
                monthlyForecast.Add(fSum);
                monthlyActual.Add(aSum);
            }

            ViewBag.LineChartLabels = JsonSerializer.Serialize(lineLabels);
            ViewBag.LineChartForecast = JsonSerializer.Serialize(monthlyForecast);
            ViewBag.LineChartActual = JsonSerializer.Serialize(monthlyActual);

            // 5. Pagination
            int totalRecords = filteredResults.Count;
            int totalPages = (int)Math.Ceiling(totalRecords / (double)pageSize);
            if (totalPages == 0) totalPages = 1;

            var pagedData = filteredResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentPeriod = periodFilter;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;

            // 6. Budget dropdown — populated from database (with department info)
            var budgetDropdownQuery = _context.Budgets.Include(b => b.Department).AsQueryable();
            if (tenantFilter != null)
                budgetDropdownQuery = budgetDropdownQuery.Where(b => b.TenantID == tenantFilter.Value);

            var budgets = await budgetDropdownQuery
                .Select(b => new SelectListItem
                {
                    Value = b.BudgetID.ToString(),
                    Text = (b.Department != null ? b.Department.DepartmentName : "N/A") + " — " + b.Category + " ($" + b.Amount.ToString("N0") + ")"
                }).ToListAsync();

            ViewBag.Budgets = budgets;

            // Forecast types matching the DB CHECK constraint
            ViewBag.ForecastTypes = new List<SelectListItem>
            {
                new SelectListItem { Value = "Best Case", Text = "Best Case" },
                new SelectListItem { Value = "Base Case", Text = "Base Case" },
                new SelectListItem { Value = "Worst Case", Text = "Worst Case" }
            };

            // Comparison data for the table (actual budget amounts keyed by BudgetID)
            var budgetAmounts = allBudgets.ToDictionary(b => b.BudgetID, b => b.Amount);
            ViewBag.BudgetAmounts = budgetAmounts;

            // Pass RBAC flags to view for conditional UI
            ViewBag.CanWrite  = CanWriteFinancials;
            ViewBag.CanDelete = CanDeleteRecords;
            ViewBag.RoleID    = CurrentRoleID;

            // ── Alpha Vantage: Economic Indicators ─────
            var roleId = CurrentRoleID ?? 1;
            if (roleId == Roles.SuperAdmin || roleId == Roles.Admin ||
                roleId == Roles.FinanceManager || roleId == Roles.Executive)
            {
                try
                {
                    ViewBag.EconomicData = await _alphaVantage.GetEconomicDataAsync();
                }
                catch
                {
                    ViewBag.EconomicData = null;
                }
            }

            return View(pagedData);
        }

        // POST: Forecast/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(int BudgetID, string ForecastType, decimal PredictedAmount, int Year)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Super Admin, Admin, Finance Manager can create
            if (!CanWriteFinancials) return AccessDenied();

            int tenantId = IsSuperAdmin ? (CurrentTenantID ?? 0) : CurrentTenantID!.Value;
            int userId   = CurrentUserID!.Value;

            // Derive DepartmentID from the selected Budget
            var budget = await _context.Budgets.FirstOrDefaultAsync(b => b.BudgetID == BudgetID && (IsSuperAdmin || b.TenantID == tenantId));
            if (budget == null) return RedirectToAction(nameof(Index));

            var forecast = new Forecast
            {
                DepartmentID = budget.DepartmentID,
                TenantID = budget.TenantID,
                BudgetID = BudgetID,
                ForecastType = ForecastType,
                PredictedAmount = PredictedAmount,
                Year = Year,
                CreatedBy = userId,
                CreatedAt = DateTime.Now
            };

            _context.Forecasts.Add(forecast);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST: Forecast/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, int BudgetID, string ForecastType, decimal PredictedAmount, int Year)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Super Admin, Admin, Finance Manager can edit
            if (!CanWriteFinancials) return AccessDenied();

            int? tenantFilter = GetTenantFilter();

            var existing = await _context.Forecasts.FirstOrDefaultAsync(f => f.ForecastID == id && (tenantFilter == null || f.TenantID == tenantFilter.Value));
            if (existing != null)
            {
                // Derive DepartmentID from the selected Budget
                var budget = await _context.Budgets.FirstOrDefaultAsync(b => b.BudgetID == BudgetID && (tenantFilter == null || b.TenantID == tenantFilter.Value));
                if (budget != null)
                {
                    existing.BudgetID = BudgetID;
                    existing.DepartmentID = budget.DepartmentID;
                    existing.ForecastType = ForecastType;
                    existing.PredictedAmount = PredictedAmount;
                    existing.Year = Year;

                    _context.Forecasts.Update(existing);
                    await _context.SaveChangesAsync();
                }
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: Forecast/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Super Admin and Admin can delete
            if (!CanDeleteRecords) return AccessDenied();

            int? tenantFilter = GetTenantFilter();

            var existing = await _context.Forecasts.FirstOrDefaultAsync(f => f.ForecastID == id && (tenantFilter == null || f.TenantID == tenantFilter.Value));
            if (existing != null)
            {
                _context.Forecasts.Remove(existing);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        // GET: Forecast/ChartData — JSON endpoint for dynamic Chart.js updates
        [HttpGet]
        public async Task<IActionResult> ChartData()
        {
            if (!IsAuthenticated) return Unauthorized();

            int? tenantFilter = GetTenantFilter();

            var query = _context.Forecasts
                .Include(f => f.Department)
                .AsQueryable();

            if (tenantFilter != null)
                query = query.Where(f => f.TenantID == tenantFilter.Value);

            var forecasts = await query
                .Select(f => new
                {
                    Department = f.Department != null ? f.Department.DepartmentName : "N/A",
                    f.ForecastType,
                    f.PredictedAmount,
                    f.Year,
                    f.CreatedAt
                })
                .ToListAsync();

            return Json(forecasts);
        }
    }
}
