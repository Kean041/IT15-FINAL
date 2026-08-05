using FinSight.Data;
using FinSight.Models;
using FinSight.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace FinSight.Controllers
{
    public class ScenarioController : BaseController
    {
        private readonly FinSightDbContext _context;
        private readonly ILogger<ScenarioController> _logger;

        public ScenarioController(FinSightDbContext context, ILogger<ScenarioController> logger)
        {
            _context = context;
            _logger = logger;
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

            try
            {
            if (!HttpContext.Items.ContainsKey("__UseLegacyScenarioPlanning"))
            {
                return await RenderScenarioPlanningCompatibilityAsync(
                    searchString,
                    periodFilter,
                    page,
                    tenantFilter,
                    pageSize);
            }

            var query = _context.Scenarios
                .AsNoTracking()
                .Include(s => s.ScenarioDetails)
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
            await PopulateScenarioCreatorsAsync(allResults);

            // --- Analytics ---
            int    totalCount       = allResults.Count;
            decimal totalAdjusted  = allResults.SelectMany(s => s.ScenarioDetails).Sum(sd => sd.AdjustedAmount);
            int    deptsCovered    = allResults.SelectMany(s => s.ScenarioDetails)
                                               .Select(sd => sd.DepartmentID).Distinct().Count();

            ViewBag.TotalScenarios  = totalCount;
            ViewBag.TotalAdjusted   = totalAdjusted;
            ViewBag.DeptsCovered    = deptsCovered;

            // --- Chart: Budget.Amount vs AdjustedAmount per Department ---
            var budgetQuery = _context.Budgets.AsNoTracking().AsQueryable();
            if (tenantFilter != null)
                budgetQuery = budgetQuery.Where(b => b.TenantID == tenantFilter.Value);

            var allBudgets = await budgetQuery
                .Select(b => new
                {
                    b.DepartmentID,
                    b.Amount,
                    DepartmentName = b.Department != null ? b.Department.DepartmentName : null
                })
                .ToListAsync();

            var scenarioDepartmentIds = allResults
                .SelectMany(s => s.ScenarioDetails)
                .Select(sd => sd.DepartmentID)
                .Distinct()
                .ToList();

            var scenarioDepartmentNames = scenarioDepartmentIds.Count == 0
                ? new Dictionary<int, string>()
                : await _context.Departments
                    .AsNoTracking()
                    .Where(d =>
                        scenarioDepartmentIds.Contains(d.DepartmentID) &&
                        (tenantFilter == null || d.TenantID == tenantFilter.Value))
                    .Select(d => new
                    {
                        d.DepartmentID,
                        d.DepartmentName
                    })
                    .ToDictionaryAsync(d => d.DepartmentID, d => d.DepartmentName);

            var deptNames = allResults
                .SelectMany(s => s.ScenarioDetails)
                .Select(sd => scenarioDepartmentNames.GetValueOrDefault(sd.DepartmentID))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Union(allBudgets
                    .Select(b => b.DepartmentName)
                    .Where(name => !string.IsNullOrWhiteSpace(name)))
                .Cast<string>()
                .Distinct()
                .OrderBy(d => d)
                .ToList();

            var chartOriginals  = new List<decimal>();
            var chartAdjusteds  = new List<decimal>();

            foreach (var dept in deptNames)
            {
                chartOriginals.Add(
                    allBudgets.Where(b => b.DepartmentName == dept)
                              .Sum(b => b.Amount));
                chartAdjusteds.Add(
                    allResults.SelectMany(s => s.ScenarioDetails)
                              .Where(sd => scenarioDepartmentNames.GetValueOrDefault(sd.DepartmentID) == dept)
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
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Scenario planning page failed for user {UserID}, role {RoleID}, tenant {TenantID}.",
                    CurrentUserID,
                    CurrentRoleID,
                    tenantFilter);

                PopulatePlanningFallbackViewBags(searchString, periodFilter, page);
                return View(new List<Scenario>());
            }
        }

        private void PopulatePlanningFallbackViewBags(string searchString, string periodFilter, int page)
        {
            ViewBag.TotalScenarios = 0;
            ViewBag.TotalAdjusted = 0m;
            ViewBag.DeptsCovered = 0;
            ViewBag.ChartLabels = "[]";
            ViewBag.ChartOriginals = "[]";
            ViewBag.ChartAdjusteds = "[]";
            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentPeriod = periodFilter;
            ViewBag.CurrentPage = page < 1 ? 1 : page;
            ViewBag.TotalPages = 1;
            ViewBag.Budgets = new List<SelectListItem>();
            ViewBag.CanWrite = CanWriteFinancials;
            ViewBag.CanDelete = CanDeleteRecords;
            ViewBag.RoleID = CurrentRoleID;
        }

        private async Task<IActionResult> RenderScenarioPlanningCompatibilityAsync(
            string searchString,
            string periodFilter,
            int page,
            int? tenantFilter,
            int pageSize)
        {
            await EnsureFinanceSchemaBestEffortAsync();

            if (page < 1) page = 1;

            var allResults = await LoadScenarioRowsAsync(tenantFilter, searchString, periodFilter);
            var detailRows = await LoadScenarioDetailRowsAsync(tenantFilter);
            var detailLookup = detailRows
                .GroupBy(d => d.ScenarioID)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (var scenario in allResults)
            {
                scenario.ScenarioDetails = detailLookup.TryGetValue(scenario.ScenarioID, out var scenarioDetails)
                    ? scenarioDetails
                    : new List<ScenarioDetail>();
            }

            await ApplyScenarioCreatorNamesBestEffortAsync(allResults, tenantFilter);

            var totalCount = allResults.Count;
            var totalAdjusted = allResults.SelectMany(s => s.ScenarioDetails).Sum(sd => sd.AdjustedAmount);
            var deptsCovered = allResults.SelectMany(s => s.ScenarioDetails)
                .Select(sd => sd.DepartmentID)
                .Distinct()
                .Count();

            ViewBag.TotalScenarios = totalCount;
            ViewBag.TotalAdjusted = totalAdjusted;
            ViewBag.DeptsCovered = deptsCovered;

            var budgetRows = await LoadScenarioBudgetRowsAsync(tenantFilter);
            var departmentNames = await LoadDepartmentNameLookupAsync(tenantFilter);

            var deptNames = allResults
                .SelectMany(s => s.ScenarioDetails)
                .Select(sd => departmentNames.GetValueOrDefault(sd.DepartmentID))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Union(budgetRows
                    .Select(b => b.DepartmentName)
                    .Where(name => !string.IsNullOrWhiteSpace(name)))
                .Cast<string>()
                .Distinct()
                .OrderBy(name => name)
                .ToList();

            var chartOriginals = new List<decimal>();
            var chartAdjusteds = new List<decimal>();

            foreach (var departmentName in deptNames)
            {
                chartOriginals.Add(budgetRows
                    .Where(b => b.DepartmentName == departmentName)
                    .Sum(b => b.Amount));

                chartAdjusteds.Add(allResults
                    .SelectMany(s => s.ScenarioDetails)
                    .Where(sd => departmentNames.GetValueOrDefault(sd.DepartmentID) == departmentName)
                    .Sum(sd => sd.AdjustedAmount));
            }

            ViewBag.ChartLabels = JsonSerializer.Serialize(deptNames);
            ViewBag.ChartOriginals = JsonSerializer.Serialize(chartOriginals);
            ViewBag.ChartAdjusteds = JsonSerializer.Serialize(chartAdjusteds);

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages == 0) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var pagedData = allResults
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.CurrentSearch = searchString;
            ViewBag.CurrentPeriod = periodFilter;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.Budgets = budgetRows.Select(b => new SelectListItem
            {
                Value = b.BudgetID.ToString(),
                Text = $"{b.DepartmentName} - {b.Category} (PHP {b.Amount:N0})"
            }).ToList();
            ViewBag.CanWrite = CanWriteFinancials;
            ViewBag.CanDelete = CanDeleteRecords;
            ViewBag.RoleID = CurrentRoleID;

            return View(pagedData);
        }

        private async Task EnsureFinanceSchemaBestEffortAsync()
        {
            try
            {
                await DbInitializer.EnsureExpenseSchemaAsync(_context, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Finance schema repair failed before loading scenario planning; continuing with compatibility queries.");
            }
        }

        private async Task<List<Scenario>> LoadScenarioRowsAsync(int? tenantFilter, string searchString, string periodFilter)
        {
            const string sql = @"
                SELECT
                    ScenarioID,
                    ScenarioName,
                    [Description],
                    TenantID,
                    CreatedBy,
                    CreatedAt,
                    AppliedInflation,
                    AppliedExchangeRate
                FROM Scenarios
                WHERE (@TenantID IS NULL OR TenantID = @TenantID)
                  AND (
                        @Search IS NULL
                        OR ScenarioName LIKE @Search
                        OR [Description] LIKE @Search
                  )
                  AND (@PeriodStart IS NULL OR CreatedAt >= @PeriodStart)
                ORDER BY CreatedAt DESC, ScenarioID DESC";

            var searchTerm = string.IsNullOrWhiteSpace(searchString) ? null : $"%{searchString.Trim()}%";
            DateTime? periodStart = periodFilter switch
            {
                "Day" => DateTime.Today,
                "Week" => DateTime.Now.AddDays(-7),
                "Month" => DateTime.Now.AddMonths(-1),
                _ => null
            };

            return await ScenarioQueryAsync(sql, command =>
            {
                AddParameter(command, "@TenantID", tenantFilter);
                AddParameter(command, "@Search", searchTerm);
                AddParameter(command, "@PeriodStart", periodStart);
            }, reader => new Scenario
            {
                ScenarioID = GetInt32(reader, "ScenarioID"),
                ScenarioName = GetString(reader, "ScenarioName", "Scenario"),
                Description = GetStringOrNull(reader, "Description"),
                TenantID = GetInt32(reader, "TenantID"),
                CreatedBy = GetInt32(reader, "CreatedBy"),
                CreatedAt = GetDateTime(reader, "CreatedAt", DateTime.Now),
                AppliedInflation = GetNullableDecimal(reader, "AppliedInflation"),
                AppliedExchangeRate = GetNullableDecimal(reader, "AppliedExchangeRate"),
                ScenarioDetails = new List<ScenarioDetail>()
            });
        }

        private async Task<List<ScenarioDetail>> LoadScenarioDetailRowsAsync(int? tenantFilter)
        {
            const string sql = @"
                SELECT
                    ScenarioDetailID,
                    ScenarioID,
                    BudgetID,
                    DepartmentID,
                    AdjustedAmount,
                    TenantID,
                    CreatedAt
                FROM ScenarioDetails
                WHERE (@TenantID IS NULL OR TenantID = @TenantID)";

            return await ScenarioQueryAsync(sql, command =>
            {
                AddParameter(command, "@TenantID", tenantFilter);
            }, reader => new ScenarioDetail
            {
                ScenarioDetailID = GetInt32(reader, "ScenarioDetailID"),
                ScenarioID = GetInt32(reader, "ScenarioID"),
                BudgetID = GetInt32(reader, "BudgetID"),
                DepartmentID = GetInt32(reader, "DepartmentID"),
                AdjustedAmount = GetDecimal(reader, "AdjustedAmount"),
                TenantID = GetInt32(reader, "TenantID"),
                CreatedAt = GetDateTime(reader, "CreatedAt", DateTime.Now)
            });
        }

        private async Task<List<ScenarioBudgetRow>> LoadScenarioBudgetRowsAsync(int? tenantFilter)
        {
            const string sql = @"
                SELECT
                    b.BudgetID,
                    b.DepartmentID,
                    b.Category,
                    b.Amount,
                    COALESCE(d.DepartmentName, 'General') AS DepartmentName
                FROM Budgets b
                LEFT JOIN Departments d ON d.DepartmentID = b.DepartmentID
                WHERE (@TenantID IS NULL OR b.TenantID = @TenantID)
                ORDER BY COALESCE(d.DepartmentName, 'General'), b.Category";

            return await ScenarioQueryAsync(sql, command =>
            {
                AddParameter(command, "@TenantID", tenantFilter);
            }, reader => new ScenarioBudgetRow
            {
                BudgetID = GetInt32(reader, "BudgetID"),
                DepartmentID = GetInt32(reader, "DepartmentID"),
                Category = GetString(reader, "Category", "General"),
                Amount = GetDecimal(reader, "Amount"),
                DepartmentName = GetString(reader, "DepartmentName", "General")
            });
        }

        private async Task<Dictionary<int, string>> LoadDepartmentNameLookupAsync(int? tenantFilter)
        {
            const string sql = @"
                SELECT DepartmentID, DepartmentName
                FROM Departments
                WHERE (@TenantID IS NULL OR TenantID = @TenantID)";

            var rows = await ScenarioQueryAsync(sql, command =>
            {
                AddParameter(command, "@TenantID", tenantFilter);
            }, reader => new KeyValuePair<int, string>(
                GetInt32(reader, "DepartmentID"),
                GetString(reader, "DepartmentName", "General")));

            return rows
                .GroupBy(row => row.Key)
                .ToDictionary(group => group.Key, group => group.First().Value);
        }

        private async Task ApplyScenarioCreatorNamesBestEffortAsync(List<Scenario> scenarios, int? tenantFilter)
        {
            if (scenarios.Count == 0)
                return;

            try
            {
                var names = await LoadScenarioCreatorNamesAsync(tenantFilter);
                foreach (var scenario in scenarios)
                {
                    if (!names.TryGetValue(scenario.CreatedBy, out var fullName))
                        continue;

                    scenario.Creator = new User
                    {
                        UserID = scenario.CreatedBy,
                        FullName = fullName
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Scenario creator names could not be loaded; rendering scenarios without creator names.");
            }
        }

        private async Task<Dictionary<int, string>> LoadScenarioCreatorNamesAsync(int? tenantFilter)
        {
            const string sql = @"
                SELECT UserID, FullName
                FROM Users
                WHERE (@TenantID IS NULL OR TenantID = @TenantID)";

            var rows = await ScenarioQueryAsync(sql, command =>
            {
                AddParameter(command, "@TenantID", tenantFilter);
            }, reader => new KeyValuePair<int, string>(
                GetInt32(reader, "UserID"),
                GetString(reader, "FullName", string.Empty)));

            return rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Value))
                .GroupBy(row => row.Key)
                .ToDictionary(group => group.Key, group => group.First().Value);
        }

        private async Task<List<object>> LoadScenarioComparisonRowsAsync(int? tenantFilter)
        {
            const string sql = @"
                SELECT
                    COALESCE(s.ScenarioName, 'N/A') AS ScenarioName,
                    COALESCE(d.DepartmentName, 'N/A') AS Department,
                    COALESCE(b.Category, 'N/A') AS BudgetCategory,
                    COALESCE(b.Amount, 0) AS OriginalAmount,
                    sd.AdjustedAmount,
                    sd.CreatedAt
                FROM ScenarioDetails sd
                LEFT JOIN Scenarios s ON s.ScenarioID = sd.ScenarioID
                LEFT JOIN Departments d ON d.DepartmentID = sd.DepartmentID
                LEFT JOIN Budgets b ON b.BudgetID = sd.BudgetID
                WHERE (@TenantID IS NULL OR sd.TenantID = @TenantID)
                ORDER BY sd.CreatedAt DESC, sd.ScenarioDetailID DESC";

            return await ScenarioQueryAsync<object>(sql, command =>
            {
                AddParameter(command, "@TenantID", tenantFilter);
            }, reader =>
            {
                var originalAmount = GetDecimal(reader, "OriginalAmount");
                var adjustedAmount = GetDecimal(reader, "AdjustedAmount");

                return new
                {
                    ScenarioName = GetString(reader, "ScenarioName", "N/A"),
                    Department = GetString(reader, "Department", "N/A"),
                    BudgetCategory = GetString(reader, "BudgetCategory", "N/A"),
                    OriginalAmount = originalAmount,
                    AdjustedAmount = adjustedAmount,
                    Difference = adjustedAmount - originalAmount,
                    CreatedAt = GetDateTime(reader, "CreatedAt", DateTime.Now)
                };
            });
        }

        private async Task<List<T>> ScenarioQueryAsync<T>(string sql, Action<DbCommand> configure, Func<DbDataReader, T> map)
        {
            var results = new List<T>();
            var connection = _context.Database.GetDbConnection();
            var shouldClose = connection.State != ConnectionState.Open;

            if (shouldClose)
                await connection.OpenAsync();

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = sql;
                configure(command);

                using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    results.Add(map(reader));
                }
            }
            finally
            {
                if (shouldClose)
                    await connection.CloseAsync();
            }

            return results;
        }

        private static void AddParameter(DbCommand command, string name, object? value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        private static int GetInt32(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static decimal GetDecimal(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));
        }

        private static decimal? GetNullableDecimal(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : Convert.ToDecimal(reader.GetValue(ordinal));
        }

        private static DateTime GetDateTime(DbDataReader reader, string columnName, DateTime defaultValue)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? defaultValue : Convert.ToDateTime(reader.GetValue(ordinal));
        }

        private static string GetString(DbDataReader reader, string columnName, string defaultValue)
        {
            return GetStringOrNull(reader, columnName) ?? defaultValue;
        }

        private static string? GetStringOrNull(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : Convert.ToString(reader.GetValue(ordinal));
        }

        private sealed class ScenarioBudgetRow
        {
            public int BudgetID { get; set; }
            public int DepartmentID { get; set; }
            public string Category { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public string DepartmentName { get; set; } = "General";
        }

        private async Task PopulateScenarioCreatorsAsync(List<Scenario> scenarios)
        {
            var creatorIds = scenarios
                .Select(s => s.CreatedBy)
                .Where(id => id > 0)
                .Distinct()
                .ToList();

            if (creatorIds.Count == 0)
                return;

            var creatorNames = await _context.Users
                .AsNoTracking()
                .Where(u => creatorIds.Contains(u.UserID))
                .Select(u => new
                {
                    u.UserID,
                    u.FullName
                })
                .ToDictionaryAsync(u => u.UserID, u => u.FullName);

            foreach (var scenario in scenarios)
            {
                if (!creatorNames.TryGetValue(scenario.CreatedBy, out var fullName))
                    continue;

                scenario.Creator = new User
                {
                    UserID = scenario.CreatedBy,
                    FullName = fullName
                };
            }
        }

        // ─────────────────────────────────────────────
        // POST: Scenario/Create
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string ScenarioName,
            string? Description,
            decimal? AppliedInflation,
            decimal? AppliedExchangeRate,
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
                AppliedInflation = AppliedInflation,
                AppliedExchangeRate = AppliedExchangeRate,
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
            decimal? AppliedInflation,
            decimal? AppliedExchangeRate,
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
            existing.AppliedInflation = AppliedInflation;
            existing.AppliedExchangeRate = AppliedExchangeRate;

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

            if (!HttpContext.Items.ContainsKey("__UseLegacyScenarioComparisonData"))
            {
                try
                {
                    await EnsureFinanceSchemaBestEffortAsync();
                    var compatibleDetails = await LoadScenarioComparisonRowsAsync(tenantFilter);
                    return Json(compatibleDetails);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scenario comparison JSON failed for tenant {TenantID}.", tenantFilter);
                    return Json(Array.Empty<object>());
                }
            }

            var query = _context.ScenarioDetails
                .AsNoTracking()
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
                .AsNoTracking()
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

        // ─────────────────────────────────────────────
        // GET: Scenario/RunSimulation/5
        // ─────────────────────────────────────────────
        public async Task<IActionResult> RunSimulation(int id)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanAccessScenario) return AccessDenied();

            int? tenantFilter = GetTenantFilter();

            var scenario = await _context.Scenarios
                .Include(s => s.ScenarioDetails)
                .ThenInclude(sd => sd.Budget)
                .ThenInclude(b => b.Department)
                .FirstOrDefaultAsync(s => s.ScenarioID == id && (tenantFilter == null || s.TenantID == tenantFilter.Value));

            if (scenario == null)
            {
                TempData["Error"] = "Scenario not found.";
                return RedirectToAction(nameof(Planning));
            }

            // Fetch actual expenses for the budgets in this scenario
            var budgetIds = scenario.ScenarioDetails.Select(sd => sd.BudgetID).ToList();
            var expenses = await _context.Expenses
                .Where(e => budgetIds.Contains(e.BudgetID))
                .GroupBy(e => e.BudgetID)
                .Select(g => new { BudgetID = g.Key, TotalExpenses = g.Sum(x => x.Amount) })
                .ToDictionaryAsync(x => x.BudgetID, x => x.TotalExpenses);

            var simulationResults = new List<FinSight.Models.ViewModels.DynamicForecastViewModel>();
            decimal inflationToApply = scenario.AppliedInflation.HasValue ? (scenario.AppliedInflation.Value / 100m) : 0m;
            decimal exchangeRate = scenario.AppliedExchangeRate ?? 1m;

            DateTime today = DateTime.Now;

            foreach (var detail in scenario.ScenarioDetails)
            {
                if (detail.Budget == null) continue;

                var totalExpenses = expenses.GetValueOrDefault(detail.BudgetID, 0m);
                var simulatedBudgetAmount = detail.AdjustedAmount; // The adjusted amount becomes the new budget

                // Simple run rate
                DateTime startOfYear = new DateTime(detail.Budget.Year, 1, 1);
                DateTime currentDateToUse = (detail.Budget.Year == today.Year) ? today : new DateTime(detail.Budget.Year, 12, 31);
                
                int elapsedDays = (currentDateToUse - startOfYear).Days;
                if (elapsedDays <= 0) elapsedDays = 1;
                int totalDaysInYear = DateTime.IsLeapYear(detail.Budget.Year) ? 366 : 365;

                decimal dailyRunRate = totalExpenses / elapsedDays;
                decimal baseRunRate = dailyRunRate * totalDaysInYear;
                
                // Project future expenses by applying inflation to the remaining expected expenses
                decimal futureExpenses = baseRunRate * (1 + inflationToApply);
                
                // Since this is a simulation, we might also have an exchange rate multiplier if costs are in foreign currency,
                // but let's just apply it to future expenses as a simple illustration if they want to simulate FX impact.
                futureExpenses *= exchangeRate;

                decimal futureUtilization = simulatedBudgetAmount > 0 ? (futureExpenses / simulatedBudgetAmount) * 100m : 0m;
                decimal projectedRemaining = simulatedBudgetAmount - futureExpenses;
                decimal predictedVariance = simulatedBudgetAmount - futureExpenses;

                string status;
                if (futureExpenses > simulatedBudgetAmount)
                    status = "Projected Over Budget";
                else if (futureExpenses < simulatedBudgetAmount)
                    status = "Projected Under Budget";
                else
                    status = "Projected On Track";

                simulationResults.Add(new FinSight.Models.ViewModels.DynamicForecastViewModel
                {
                    BudgetID = detail.BudgetID,
                    DepartmentID = detail.DepartmentID,
                    DepartmentName = detail.Budget.Department?.DepartmentName ?? "Unknown",
                    Category = detail.Budget.Category,
                    BudgetAmount = simulatedBudgetAmount, // Showing the simulated budget here
                    CurrentExpenses = totalExpenses,
                    CurrentUtilization = simulatedBudgetAmount > 0 ? (totalExpenses / simulatedBudgetAmount) * 100m : 0m,
                    RunRate = baseRunRate,
                    AppliedInflationRate = inflationToApply,
                    FutureExpenses = futureExpenses,
                    FutureBudgetUtilization = futureUtilization,
                    ProjectedRemainingBudget = projectedRemaining,
                    PredictedVariance = predictedVariance,
                    Status = status,
                    Year = detail.Budget.Year
                });
            }

            ViewBag.ScenarioName = scenario.ScenarioName;
            ViewBag.ScenarioId = scenario.ScenarioID;
            ViewBag.AppliedInflation = scenario.AppliedInflation;
            ViewBag.AppliedExchangeRate = scenario.AppliedExchangeRate;

            return View("SimulationResult", simulationResults);
        }
    }
}
