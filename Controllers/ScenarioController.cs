using FinSight.Data;
using FinSight.Models;
using FinSight.Helpers;
using Microsoft.AspNetCore.Http;
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
    public class ScenarioController : BaseController
    {
        private readonly FinSightDbContext _context;

        public ScenarioController(FinSightDbContext context)
        {
            _context = context;
        }

        // ─────────────────────────────────────────────
        // GET: Scenario/Planning
        // ─────────────────────────────────────────────
        public async Task<IActionResult> Planning(string searchString, string periodFilter, int page = 1)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: SuperAdmin, Admin, Finance Manager, Department Head can access Scenario Planning
            if (!CanAccessScenario)
                return AccessDenied();

            int? tenantFilter = GetTenantFilter();
            int pageSize = 10;

            var query = _context.Scenarios
                .Include(s => s.Creator)
                .Include(s => s.ScenarioDetails)
                    .ThenInclude(sd => sd.Budget)
                .Include(s => s.ScenarioDetails)
                    .ThenInclude(sd => sd.Department)
                .AsQueryable();

            // Apply tenant filter (Super Admin sees all)
            if (tenantFilter != null)
            {
                query = query.Where(s => s.TenantID == tenantFilter.Value);
            }

            // --- Text search ---
            if (!string.IsNullOrWhiteSpace(searchString))
            {
                query = query.Where(s =>
                    s.ScenarioName.Contains(searchString) ||
                    (s.Description != null && s.Description.Contains(searchString)));
            }

            // --- Period filter ---
            if (!string.IsNullOrWhiteSpace(periodFilter))
            {
                DateTime now = DateTime.Now;
                query = periodFilter switch
                {
                    "Day"   => query.Where(s => s.CreatedAt.Date == now.Date),
                    "Week"  => query.Where(s => s.CreatedAt >= now.AddDays(-7)),
                    "Month" => query.Where(s => s.CreatedAt >= now.AddMonths(-1)),
                    _       => query
                };
            }

            var orderedQuery = query.OrderByDescending(s => s.CreatedAt);
            var allResults   = await orderedQuery.ToListAsync();

            // --- Analytics ---
            int    totalCount       = allResults.Count;
            decimal totalAdjusted  = allResults.SelectMany(s => s.ScenarioDetails).Sum(sd => sd.AdjustedAmount);
            int    deptsCovered    = allResults.SelectMany(s => s.ScenarioDetails)
                                               .Select(sd => sd.DepartmentID).Distinct().Count();

            ViewBag.TotalScenarios  = totalCount;
            ViewBag.TotalAdjusted   = totalAdjusted;
            ViewBag.DeptsCovered    = deptsCovered;

            // --- Chart: Budget.Amount vs AdjustedAmount per Department ---
            var budgetQuery = _context.Budgets.Include(b => b.Department).AsQueryable();
            if (tenantFilter != null)
                budgetQuery = budgetQuery.Where(b => b.TenantID == tenantFilter.Value);

            var allBudgets = await budgetQuery.ToListAsync();

            var deptNames = allResults
                .SelectMany(s => s.ScenarioDetails)
                .Where(sd => sd.Department != null)
                .Select(sd => sd.Department!.DepartmentName)
                .Union(allBudgets.Where(b => b.Department != null).Select(b => b.Department!.DepartmentName))
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var chartOriginals  = new List<decimal>();
            var chartAdjusteds  = new List<decimal>();

            foreach (var dept in deptNames)
            {
                chartOriginals.Add(
                    allBudgets.Where(b => b.Department != null && b.Department.DepartmentName == dept)
                              .Sum(b => b.Amount));
                chartAdjusteds.Add(
                    allResults.SelectMany(s => s.ScenarioDetails)
                              .Where(sd => sd.Department != null && sd.Department.DepartmentName == dept)
                              .Sum(sd => sd.AdjustedAmount));
            }

            ViewBag.ChartLabels    = JsonSerializer.Serialize(deptNames);
            ViewBag.ChartOriginals = JsonSerializer.Serialize(chartOriginals);
            ViewBag.ChartAdjusteds = JsonSerializer.Serialize(chartAdjusteds);

            // --- Pagination ---
            int totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages == 0) totalPages = 1;
            var pagedData = allResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentPeriod = periodFilter;
            ViewBag.CurrentPage   = page;
            ViewBag.TotalPages    = totalPages;

            // --- Dropdowns for Create/Edit modals ---
            await PopulateDropdownsAsync(tenantFilter);

            // Pass RBAC flags to view for conditional UI
            ViewBag.CanWrite  = CanWriteFinancials;
            ViewBag.CanDelete = CanDeleteRecords;
            ViewBag.RoleID    = CurrentRoleID;

            return View(pagedData);
        }

        // ─────────────────────────────────────────────
        // POST: Scenario/Create
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string ScenarioName,
            string? Description,
            List<int>    BudgetIDs,
            List<decimal> AdjustedAmounts)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Super Admin, Admin, Finance Manager can create
            if (!CanWriteFinancials) return AccessDenied();

            int tenantId = IsSuperAdmin ? (CurrentTenantID ?? 0) : CurrentTenantID!.Value;
            int userId   = CurrentUserID!.Value;

            // Validate required fields
            if (string.IsNullOrWhiteSpace(ScenarioName))
            {
                TempData["Error"] = "Scenario name is required.";
                return RedirectToAction(nameof(Planning));
            }

            if (BudgetIDs == null || BudgetIDs.Count == 0)
            {
                TempData["Error"] = "At least one budget detail is required.";
                return RedirectToAction(nameof(Planning));
            }

            var scenario = new Scenario
            {
                ScenarioName = ScenarioName.Trim(),
                Description  = Description?.Trim(),
                TenantID     = tenantId,
                CreatedBy    = userId,
                CreatedAt    = DateTime.Now
            };

            _context.Scenarios.Add(scenario);
            await _context.SaveChangesAsync(); // get ScenarioID

            // Build ScenarioDetails
            var details = new List<ScenarioDetail>();
            for (int i = 0; i < BudgetIDs.Count; i++)
            {
                int budgetId = BudgetIDs[i];
                decimal adjusted = (AdjustedAmounts != null && i < AdjustedAmounts.Count)
                    ? AdjustedAmounts[i] : 0m;

                if (adjusted <= 0) continue; // skip blank rows

                var budget = await _context.Budgets
                    .FirstOrDefaultAsync(b => b.BudgetID == budgetId && (IsSuperAdmin || b.TenantID == tenantId));

                if (budget == null) continue;

                details.Add(new ScenarioDetail
                {
                    ScenarioID     = scenario.ScenarioID,
                    BudgetID       = budgetId,
                    DepartmentID   = budget.DepartmentID,
                    AdjustedAmount = adjusted,
                    TenantID       = budget.TenantID,
                    CreatedAt      = DateTime.Now
                });
            }

            if (details.Count == 0)
            {
                // Roll back the scenario header if no valid details were submitted
                _context.Scenarios.Remove(scenario);
                await _context.SaveChangesAsync();
                TempData["Error"] = "Please add at least one valid budget detail with an adjusted amount.";
                return RedirectToAction(nameof(Planning));
            }

            _context.ScenarioDetails.AddRange(details);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Scenario \"{scenario.ScenarioName}\" created successfully.";
            return RedirectToAction(nameof(Planning));
        }

        // ─────────────────────────────────────────────
        // POST: Scenario/Edit/5
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(
            int ScenarioID,
            string ScenarioName,
            string? Description,
            List<int>     BudgetIDs,
            List<decimal> AdjustedAmounts)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Super Admin, Admin, Finance Manager can edit
            if (!CanWriteFinancials) return AccessDenied();

            int? tenantFilter = GetTenantFilter();

            if (string.IsNullOrWhiteSpace(ScenarioName))
            {
                TempData["Error"] = "Scenario name is required.";
                return RedirectToAction(nameof(Planning));
            }

            var existing = await _context.Scenarios
                .Include(s => s.ScenarioDetails)
                .FirstOrDefaultAsync(s => s.ScenarioID == ScenarioID && (tenantFilter == null || s.TenantID == tenantFilter.Value));

            if (existing == null)
                return RedirectToAction(nameof(Planning));

            // Update header
            existing.ScenarioName = ScenarioName.Trim();
            existing.Description  = Description?.Trim();

            // Replace all details
            _context.ScenarioDetails.RemoveRange(existing.ScenarioDetails);

            var newDetails = new List<ScenarioDetail>();
            for (int i = 0; i < BudgetIDs.Count; i++)
            {
                int budgetId = BudgetIDs[i];
                decimal adjusted = (AdjustedAmounts != null && i < AdjustedAmounts.Count)
                    ? AdjustedAmounts[i] : 0m;

                if (adjusted <= 0) continue;

                var budget = await _context.Budgets
                    .FirstOrDefaultAsync(b => b.BudgetID == budgetId && (tenantFilter == null || b.TenantID == tenantFilter.Value));

                if (budget == null) continue;

                newDetails.Add(new ScenarioDetail
                {
                    ScenarioID     = existing.ScenarioID,
                    BudgetID       = budgetId,
                    DepartmentID   = budget.DepartmentID,
                    AdjustedAmount = adjusted,
                    TenantID       = existing.TenantID,
                    CreatedAt      = DateTime.Now
                });
            }

            _context.ScenarioDetails.AddRange(newDetails);
            _context.Scenarios.Update(existing);
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Scenario \"{existing.ScenarioName}\" updated successfully.";
            return RedirectToAction(nameof(Planning));
        }

        // ─────────────────────────────────────────────
        // POST: Scenario/Delete/5
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            // RBAC: Only Super Admin and Admin can delete
            if (!CanDeleteRecords) return AccessDenied();

            int? tenantFilter = GetTenantFilter();

            var existing = await _context.Scenarios
                .Include(s => s.ScenarioDetails)
                .FirstOrDefaultAsync(s => s.ScenarioID == id && (tenantFilter == null || s.TenantID == tenantFilter.Value));

            if (existing != null)
            {
                // ScenarioDetails are cascade-deleted by EF Core relationship config
                _context.Scenarios.Remove(existing);
                await _context.SaveChangesAsync();
                TempData["Success"] = $"Scenario \"{existing.ScenarioName}\" deleted.";
            }

            return RedirectToAction(nameof(Planning));
        }

        // ─────────────────────────────────────────────
        // GET: Scenario/ComparisonData
        // JSON endpoint — Budget.Amount vs AdjustedAmount
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> ComparisonData()
        {
            if (!IsAuthenticated) return Unauthorized();

            // RBAC: SuperAdmin, Admin, Finance Manager, Department Head
            if (!CanAccessScenario)
                return Unauthorized();

            int? tenantFilter = GetTenantFilter();

            var query = _context.ScenarioDetails
                .Include(sd => sd.Budget)
                .Include(sd => sd.Department)
                .Include(sd => sd.Scenario)
                .AsQueryable();

            if (tenantFilter != null)
                query = query.Where(sd => sd.TenantID == tenantFilter.Value);

            var details = await query
                .Select(sd => new
                {
                    ScenarioName    = sd.Scenario != null ? sd.Scenario.ScenarioName : "N/A",
                    Department      = sd.Department != null ? sd.Department.DepartmentName : "N/A",
                    BudgetCategory  = sd.Budget != null ? sd.Budget.Category : "N/A",
                    OriginalAmount  = sd.Budget != null ? sd.Budget.Amount : 0m,
                    AdjustedAmount  = sd.AdjustedAmount,
                    Difference      = sd.AdjustedAmount - (sd.Budget != null ? sd.Budget.Amount : 0m),
                    sd.CreatedAt
                })
                .ToListAsync();

            return Json(details);
        }

        // ─────────────────────────────────────────────
        // Helper: populate ViewBag dropdowns
        // ─────────────────────────────────────────────
        private async Task PopulateDropdownsAsync(int? tenantFilter)
        {
            var query = _context.Budgets
                .Include(b => b.Department)
                .AsQueryable();

            if (tenantFilter != null)
                query = query.Where(b => b.TenantID == tenantFilter.Value);

            var budgets = await query
                .Select(b => new SelectListItem
                {
                    Value = b.BudgetID.ToString(),
                    Text  = (b.Department != null ? b.Department.DepartmentName : "N/A")
                            + " — " + b.Category
                            + " ($" + b.Amount.ToString("N0") + ")"
                })
                .ToListAsync();

            ViewBag.Budgets = budgets;
        }
    }
}
