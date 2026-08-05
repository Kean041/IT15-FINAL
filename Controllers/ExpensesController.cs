using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Data;
using System.Data.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using FinSight.Data;
using FinSight.Models;
using FinSight.Helpers;

namespace FinSight.Controllers
{
    public class ExpensesController : BaseController
    {
        private readonly FinSightDbContext _db;
        private readonly ILogger<ExpensesController> _logger;

        public ExpensesController(FinSightDbContext db, ILogger<ExpensesController> logger)
        {
            _db = db;
            _logger = logger;
        }

        // ─────────────────────────────────────────────
        // RBAC helpers
        // ─────────────────────────────────────────────
        private bool CanManage => CurrentRoleID != null && Roles.CanManageExpenses(CurrentRoleID.Value);

        // ─────────────────────────────────────────────
        // GET: Expenses
        // ─────────────────────────────────────────────
        public async Task<IActionResult> Index(int? departmentId, string? status, DateTime? startDate, DateTime? endDate, string? search, int page = 1)
        {
            if (!IsAuthenticated) return RedirectToLogin();

            var roleId = CurrentRoleID ?? Roles.DepartmentHead;
            var tenantFilter = GetTenantFilter();

            try
            {
                if (!HttpContext.Items.ContainsKey("__UseLegacyExpenseIndex"))
                {
                    return await RenderExpenseIndexCompatibilityAsync(
                        roleId,
                        tenantFilter,
                        departmentId,
                        status,
                        startDate,
                        endDate,
                        search,
                        page);
                }

            var query = _db.Expenses
                .AsNoTracking()
                .AsQueryable();

            if (tenantFilter != null)
                query = query.Where(e => e.TenantID == tenantFilter.Value);

            // Department Head can only see own department
            if (roleId == Roles.DepartmentHead)
            {
                var userDept = HttpContext.Session.GetInt32("DepartmentID");
                if (userDept != null)
                    query = query.Where(e => e.DepartmentID == userDept.Value);
            }
            else if (departmentId.HasValue && departmentId.Value > 0)
            {
                query = query.Where(e => e.DepartmentID == departmentId.Value);
            }

            if (!string.IsNullOrEmpty(status))
                query = query.Where(e => e.Status == status);

            if (startDate.HasValue)
                query = query.Where(e => e.ExpenseDate >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(e => e.ExpenseDate <= endDate.Value);

            if (!string.IsNullOrEmpty(search))
                query = query.Where(e => e.ExpenseTitle.Contains(search) || e.Category.Contains(search) || e.Description.Contains(search));

            // ── KPI Calculations ──
            var totalExpenses = await query.SumAsync(e => (decimal?)e.Amount) ?? 0m;
            var totalCount = await query.CountAsync();

            // Monthly expenses (current month)
            var now = DateTime.Now;
            var monthlyExpenses = await query
                .Where(e => e.ExpenseDate.Year == now.Year && e.ExpenseDate.Month == now.Month)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;

            // Remaining budget across all linked budgets
            var budgetIds = await query.Select(e => e.BudgetID).Distinct().ToListAsync();
            var totalAllocated = 0m;
            var totalSpent = 0m;
            if (budgetIds.Any())
            {
                totalAllocated = await _db.Budgets
                    .AsNoTracking()
                    .Where(b => budgetIds.Contains(b.BudgetID))
                    .SumAsync(b => (decimal?)b.Amount) ?? 0m;

                totalSpent = await _db.Expenses
                    .AsNoTracking()
                    .Where(e => budgetIds.Contains(e.BudgetID))
                    .SumAsync(e => (decimal?)e.Amount) ?? 0m;
            }
            var remainingBudget = totalAllocated - totalSpent;

            // ── Pagination ──
            int pageSize = 15;
            if (page < 1) page = 1;

            // Keep pagination in memory for compatibility with older SQL Server versions
            // that reject EF Core's OFFSET/FETCH SQL.
            var filteredRows = await query
                .OrderByDescending(e => e.ExpenseDate)
                .ThenByDescending(e => e.ExpenseID)
                .Select(e => new
                {
                    e.ExpenseID,
                    e.BudgetRequestID,
                    e.BudgetID,
                    e.DepartmentID,
                    e.TenantID,
                    e.ExpenseTitle,
                    e.Category,
                    e.Description,
                    e.Amount,
                    e.ExpenseDate,
                    e.Status,
                    e.CreatedAt,
                    DepartmentName = e.Department != null ? e.Department.DepartmentName : null
                })
                .ToListAsync();

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages == 0) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var items = filteredRows
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new Expense
                {
                    ExpenseID = e.ExpenseID,
                    BudgetRequestID = e.BudgetRequestID,
                    BudgetID = e.BudgetID,
                    DepartmentID = e.DepartmentID,
                    TenantID = e.TenantID,
                    ExpenseTitle = e.ExpenseTitle ?? string.Empty,
                    Category = e.Category ?? string.Empty,
                    Description = e.Description ?? string.Empty,
                    Amount = e.Amount,
                    ExpenseDate = e.ExpenseDate,
                    Status = e.Status ?? "Recorded",
                    CreatedAt = e.CreatedAt,
                    Department = string.IsNullOrWhiteSpace(e.DepartmentName)
                        ? null
                        : new Department
                        {
                            DepartmentID = e.DepartmentID,
                            DepartmentName = e.DepartmentName
                        }
                })
                .ToList();

            ViewBag.TotalExpenses = totalExpenses;
            ViewBag.MonthlyExpenses = monthlyExpenses;
            ViewBag.RemainingBudget = remainingBudget;
            ViewBag.TotalAllocated = totalAllocated;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.RoleID = roleId;
            ViewBag.CanManage = CanManage;

            // Preserve filter values
            ViewBag.CurrentDepartment = departmentId;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentStartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentEndDate = endDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentSearch = search;

            if (roleId != Roles.DepartmentHead)
            {
                var depts = await _db.Departments
                    .AsNoTracking()
                    .Where(d => tenantFilter == null || d.TenantID == tenantFilter)
                    .OrderBy(d => d.DepartmentName)
                    .Select(d => new
                    {
                        d.DepartmentID,
                        d.DepartmentName
                    })
                    .ToListAsync();
                ViewBag.Departments = new SelectList(depts, "DepartmentID", "DepartmentName", departmentId);
            }

            return View(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Expenses page failed for user {UserID}, role {RoleID}, tenant {TenantID}.",
                    CurrentUserID,
                    roleId,
                    tenantFilter);

                PopulateIndexFallbackViewBags(
                    roleId,
                    departmentId,
                    status,
                    startDate,
                    endDate,
                    search);

                return View(new List<Expense>());
            }
        }

        // ─────────────────────────────────────────────
        // GET: Expenses/Create
        // ─────────────────────────────────────────────
        private void PopulateIndexFallbackViewBags(
            int roleId,
            int? departmentId,
            string? status,
            DateTime? startDate,
            DateTime? endDate,
            string? search)
        {
            ViewBag.TotalExpenses = 0m;
            ViewBag.MonthlyExpenses = 0m;
            ViewBag.RemainingBudget = 0m;
            ViewBag.TotalAllocated = 0m;
            ViewBag.CurrentPage = 1;
            ViewBag.TotalPages = 1;
            ViewBag.TotalCount = 0;
            ViewBag.RoleID = roleId;
            ViewBag.CanManage = CanManage;
            ViewBag.CurrentDepartment = departmentId;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentStartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentEndDate = endDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentSearch = search;
            ViewBag.Departments = new SelectList(new List<Department>(), "DepartmentID", "DepartmentName", departmentId);
        }

        private async Task<IActionResult> RenderExpenseIndexCompatibilityAsync(
            int roleId,
            int? tenantFilter,
            int? departmentId,
            string? status,
            DateTime? startDate,
            DateTime? endDate,
            string? search,
            int page)
        {
            await EnsureFinanceSchemaBestEffortAsync();

            var scopedDepartmentId = roleId == Roles.DepartmentHead
                ? HttpContext.Session.GetInt32("DepartmentID")
                : departmentId;

            var filteredRows = await LoadExpenseRowsAsync(
                tenantFilter,
                scopedDepartmentId,
                status,
                startDate,
                endDate,
                search);

            var totalExpenses = filteredRows.Sum(e => e.Amount);
            var totalCount = filteredRows.Count;

            var now = DateTime.Now;
            var monthlyExpenses = filteredRows
                .Where(e => e.ExpenseDate.Year == now.Year && e.ExpenseDate.Month == now.Month)
                .Sum(e => e.Amount);

            var budgetIds = filteredRows
                .Select(e => e.BudgetID)
                .Where(id => id > 0)
                .Distinct()
                .ToHashSet();

            var totalAllocated = 0m;
            var totalSpent = 0m;
            if (budgetIds.Count > 0)
            {
                var budgetAmounts = await LoadBudgetAmountsAsync(tenantFilter);
                var expenseTotalsByBudget = await LoadExpenseTotalsByBudgetAsync(tenantFilter);

                totalAllocated = budgetAmounts
                    .Where(b => budgetIds.Contains(b.Key))
                    .Sum(b => b.Value);

                totalSpent = expenseTotalsByBudget
                    .Where(e => budgetIds.Contains(e.Key))
                    .Sum(e => e.Value);
            }

            var remainingBudget = totalAllocated - totalSpent;

            const int pageSize = 15;
            if (page < 1) page = 1;

            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            if (totalPages == 0) totalPages = 1;
            if (page > totalPages) page = totalPages;

            var items = filteredRows
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.TotalExpenses = totalExpenses;
            ViewBag.MonthlyExpenses = monthlyExpenses;
            ViewBag.RemainingBudget = remainingBudget;
            ViewBag.TotalAllocated = totalAllocated;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;
            ViewBag.RoleID = roleId;
            ViewBag.CanManage = CanManage;
            ViewBag.CurrentDepartment = departmentId;
            ViewBag.CurrentStatus = status;
            ViewBag.CurrentStartDate = startDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentEndDate = endDate?.ToString("yyyy-MM-dd");
            ViewBag.CurrentSearch = search;

            if (roleId != Roles.DepartmentHead)
            {
                var depts = await LoadDepartmentOptionsAsync(tenantFilter);
                ViewBag.Departments = new SelectList(depts, "DepartmentID", "DepartmentName", departmentId);
            }
            else
            {
                ViewBag.Departments = new SelectList(new List<Department>(), "DepartmentID", "DepartmentName");
            }

            return View(items);
        }

        private async Task EnsureFinanceSchemaBestEffortAsync()
        {
            try
            {
                await DbInitializer.EnsureExpenseSchemaAsync(_db, _logger);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Finance schema repair failed before loading the expense page; continuing with compatibility queries.");
            }
        }

        private async Task<List<Expense>> LoadExpenseRowsAsync(
            int? tenantFilter,
            int? departmentId,
            string? status,
            DateTime? startDate,
            DateTime? endDate,
            string? search)
        {
            const string sql = @"
                SELECT
                    e.ExpenseID,
                    e.BudgetRequestID,
                    e.BudgetID,
                    e.DepartmentID,
                    e.TenantID,
                    e.ExpenseTitle,
                    e.Category,
                    e.[Description],
                    e.Amount,
                    e.ExpenseDate,
                    e.[Status],
                    d.DepartmentName
                FROM Expenses e
                LEFT JOIN Departments d ON d.DepartmentID = e.DepartmentID
                WHERE (@TenantID IS NULL OR e.TenantID = @TenantID)
                  AND (@DepartmentID IS NULL OR e.DepartmentID = @DepartmentID)
                  AND (@Status IS NULL OR e.[Status] = @Status)
                  AND (@StartDate IS NULL OR e.ExpenseDate >= @StartDate)
                  AND (@EndDate IS NULL OR e.ExpenseDate < @EndDate)
                  AND (
                        @Search IS NULL
                        OR e.ExpenseTitle LIKE @Search
                        OR e.Category LIKE @Search
                        OR e.[Description] LIKE @Search
                  )
                ORDER BY e.ExpenseDate DESC, e.ExpenseID DESC";

            var searchTerm = string.IsNullOrWhiteSpace(search) ? null : $"%{search.Trim()}%";

            return await ExpenseQueryAsync(sql, command =>
            {
                AddParameter(command, "@TenantID", tenantFilter);
                AddParameter(command, "@DepartmentID", departmentId.HasValue && departmentId.Value > 0 ? departmentId.Value : null);
                AddParameter(command, "@Status", string.IsNullOrWhiteSpace(status) ? null : status.Trim());
                AddParameter(command, "@StartDate", startDate?.Date);
                AddParameter(command, "@EndDate", endDate?.Date.AddDays(1));
                AddParameter(command, "@Search", searchTerm);
            }, reader =>
            {
                var departmentName = GetStringOrNull(reader, "DepartmentName");
                var departmentKey = GetInt32(reader, "DepartmentID");
                var expenseDate = GetDateTime(reader, "ExpenseDate", DateTime.Now);

                return new Expense
                {
                    ExpenseID = GetInt32(reader, "ExpenseID"),
                    BudgetRequestID = GetNullableInt32(reader, "BudgetRequestID"),
                    BudgetID = GetInt32(reader, "BudgetID"),
                    DepartmentID = departmentKey,
                    TenantID = GetInt32(reader, "TenantID"),
                    ExpenseTitle = GetString(reader, "ExpenseTitle", "Expense"),
                    Category = GetString(reader, "Category", "General"),
                    Description = GetString(reader, "Description", string.Empty),
                    Amount = GetDecimal(reader, "Amount"),
                    ExpenseDate = expenseDate,
                    Year = expenseDate.Year,
                    Status = GetString(reader, "Status", "Recorded"),
                    CreatedAt = expenseDate,
                    Department = string.IsNullOrWhiteSpace(departmentName)
                        ? null
                        : new Department
                        {
                            DepartmentID = departmentKey,
                            DepartmentName = departmentName
                        }
                };
            });
        }

        private async Task<Dictionary<int, decimal>> LoadBudgetAmountsAsync(int? tenantFilter)
        {
            const string sql = @"
                SELECT BudgetID, Amount
                FROM Budgets
                WHERE (@TenantID IS NULL OR TenantID = @TenantID)";

            var rows = await ExpenseQueryAsync(sql, command =>
            {
                AddParameter(command, "@TenantID", tenantFilter);
            }, reader => new KeyValuePair<int, decimal>(
                GetInt32(reader, "BudgetID"),
                GetDecimal(reader, "Amount")));

            return rows
                .GroupBy(row => row.Key)
                .ToDictionary(group => group.Key, group => group.First().Value);
        }

        private async Task<Dictionary<int, decimal>> LoadExpenseTotalsByBudgetAsync(int? tenantFilter)
        {
            const string sql = @"
                SELECT BudgetID, SUM(Amount) AS TotalAmount
                FROM Expenses
                WHERE BudgetID > 0
                  AND (@TenantID IS NULL OR TenantID = @TenantID)
                GROUP BY BudgetID";

            var rows = await ExpenseQueryAsync(sql, command =>
            {
                AddParameter(command, "@TenantID", tenantFilter);
            }, reader => new KeyValuePair<int, decimal>(
                GetInt32(reader, "BudgetID"),
                GetDecimal(reader, "TotalAmount")));

            return rows
                .GroupBy(row => row.Key)
                .ToDictionary(group => group.Key, group => group.Sum(row => row.Value));
        }

        private async Task<List<Department>> LoadDepartmentOptionsAsync(int? tenantFilter)
        {
            const string sql = @"
                SELECT DepartmentID, DepartmentName, TenantID
                FROM Departments
                WHERE (@TenantID IS NULL OR TenantID = @TenantID)
                ORDER BY DepartmentName";

            return await ExpenseQueryAsync(sql, command =>
            {
                AddParameter(command, "@TenantID", tenantFilter);
            }, reader => new Department
            {
                DepartmentID = GetInt32(reader, "DepartmentID"),
                DepartmentName = GetString(reader, "DepartmentName", "General"),
                TenantID = GetInt32(reader, "TenantID")
            });
        }

        private async Task<List<T>> ExpenseQueryAsync<T>(string sql, Action<DbCommand> configure, Func<DbDataReader, T> map)
        {
            var results = new List<T>();
            var connection = _db.Database.GetDbConnection();
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

        private static int? GetNullableInt32(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? null : Convert.ToInt32(reader.GetValue(ordinal));
        }

        private static decimal GetDecimal(DbDataReader reader, string columnName)
        {
            var ordinal = reader.GetOrdinal(columnName);
            return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));
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

        public async Task<IActionResult> Create()
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanManage) return AccessDenied();

            await PopulateBudgetDropdown(null);
            return View();
        }

        // ─────────────────────────────────────────────
        // POST: Expenses/Create
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Expense model)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanManage) return AccessDenied();

            var tenantFilter = GetTenantFilter();

            // Clear validation state for system-assigned properties not in the form
            ModelState.Remove("DepartmentID");
            ModelState.Remove("TenantID");
            ModelState.Remove("Year");
            ModelState.Remove("CreatedBy");
            ModelState.Remove("Status");

            if (ModelState.IsValid)
            {
                var budget = await _db.Budgets
                    .Include(b => b.Department)
                    .FirstOrDefaultAsync(b =>
                        b.BudgetID == model.BudgetID &&
                        (tenantFilter == null || b.TenantID == tenantFilter.Value) &&
                        b.Status == "Active");

                if (budget == null)
                {
                    ModelState.AddModelError("BudgetID", "Selected approved budget allocation does not exist.");
                }
                else
                {
                    var linkedRequest = await ValidateLinkedRequestAsync(model.BudgetRequestID, budget.BudgetID, budget.TenantID);
                    if (model.BudgetRequestID.HasValue && linkedRequest == null)
                    {
                        ModelState.AddModelError("BudgetRequestID", "Selected budget request is not approved for this allocation.");
                    }

                    ApplyRequestDefaults(model, linkedRequest, budget);

                    await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                    var remaining = await GetRemainingBudgetAsync(budget.BudgetID);

                    if (model.Amount > remaining || remaining < 0)
                    {
                        ModelState.AddModelError("Amount", "Expense amount exceeds the remaining allocated budget.");
                        ModelState.AddModelError("Amount",
                            $"Expense amount (₱{model.Amount:N2}) exceeds remaining budget (₱{remaining:N2}).");

                        // Security log for budget overrun attempt
                        _db.AuditLogs.Add(new AuditLog
                        {
                            UserID = CurrentUserID,
                            TenantID = tenantFilter ?? budget.TenantID,
                            LogType = "Security",
                            Severity = "Warning",
                            Action = "Budget Overrun Attempt",
                            Details = $"User '{CurrentFullName}' attempted expense of ₱{model.Amount:N2} on budget '{budget.Category}' (ID:{budget.BudgetID}). Remaining: ₱{remaining:N2}.",
                            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                        });
                        await _db.SaveChangesAsync();
                        await tx.CommitAsync();
                    }
                    else if (ModelState.IsValid)
                    {
                        model.TenantID = budget.TenantID;
                        model.DepartmentID = budget.DepartmentID;
                        model.CreatedBy = CurrentUserID ?? 0;
                        model.CreatedAt = DateTime.Now;
                        model.Year = budget.Year;
                        model.Status = "Recorded";

                        _db.Expenses.Add(model);

                        // Audit Log
                        _db.AuditLogs.Add(new AuditLog
                        {
                            UserID = CurrentUserID,
                            TenantID = tenantFilter ?? budget.TenantID,
                            Action = "Expense Created",
                            Details = $"Recorded expense '{model.ExpenseTitle}' for ₱{model.Amount:N2} against budget '{budget.Category}' ({budget.Department?.DepartmentName}).",
                            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                        });

                        // Notification
                        _db.Notifications.Add(new Notification
                        {
                            TenantID = budget.TenantID,
                            Title = "New Expense Recorded",
                            Message = $"₱{model.Amount:N2} expense '{model.ExpenseTitle}' recorded against {budget.Department?.DepartmentName} budget.",
                            NotificationType = "System",
                            RedirectUrl = "/Expenses"
                        });

                        await _db.SaveChangesAsync();
                        await tx.CommitAsync();

                        TempData["Success"] = "Expense recorded successfully.";
                        return RedirectToAction(nameof(Index));
                    }
                }
            }

            await PopulateBudgetDropdown(model.BudgetID);
            return View(model);
        }

        // ─────────────────────────────────────────────
        // GET: Expenses/Edit/5
        // ─────────────────────────────────────────────
        public async Task<IActionResult> Edit(int id)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanManage) return AccessDenied();

            var tenantFilter = GetTenantFilter();
            var expense = await _db.Expenses
                .Include(e => e.Budget)
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.ExpenseID == id && (tenantFilter == null || e.TenantID == tenantFilter));

            if (expense == null) return NotFound();
            if (expense.Status == "Archived")
            {
                TempData["Error"] = "Archived expenses cannot be edited.";
                return RedirectToAction(nameof(Index));
            }

            await PopulateBudgetDropdown(expense.BudgetID);

            // Load budget details for the info panel
            var spent = await _db.Expenses
                .Where(e => e.BudgetID == expense.BudgetID && e.ExpenseID != expense.ExpenseID)
                .SumAsync(e => (decimal?)e.Amount) ?? 0m;
            ViewBag.BudgetTotal = expense.Budget?.Amount ?? 0;
            ViewBag.BudgetUsed = spent;
            ViewBag.BudgetRemaining = (expense.Budget?.Amount ?? 0) - spent;

            return View(expense);
        }

        // ─────────────────────────────────────────────
        // POST: Expenses/Edit/5
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Expense model)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanManage) return AccessDenied();

            var tenantFilter = GetTenantFilter();
            var existing = await _db.Expenses
                .FirstOrDefaultAsync(e => e.ExpenseID == id && (tenantFilter == null || e.TenantID == tenantFilter));

            if (existing == null) return NotFound();
            if (existing.Status == "Archived")
            {
                TempData["Error"] = "Archived expenses cannot be edited.";
                return RedirectToAction(nameof(Index));
            }

            // Clear validation state for system-assigned properties not in the form
            ModelState.Remove("DepartmentID");
            ModelState.Remove("TenantID");
            ModelState.Remove("Year");
            ModelState.Remove("CreatedBy");
            ModelState.Remove("Status");

            if (ModelState.IsValid)
            {
                var budget = await _db.Budgets
                    .FirstOrDefaultAsync(b =>
                        b.BudgetID == model.BudgetID &&
                        (tenantFilter == null || b.TenantID == tenantFilter.Value) &&
                        b.Status == "Active");
                if (budget == null)
                {
                    ModelState.AddModelError("BudgetID", "Selected approved budget allocation does not exist.");
                }
                else
                {
                    var linkedRequest = await ValidateLinkedRequestAsync(model.BudgetRequestID, budget.BudgetID, budget.TenantID);
                    if (model.BudgetRequestID.HasValue && linkedRequest == null)
                    {
                        ModelState.AddModelError("BudgetRequestID", "Selected budget request is not approved for this allocation.");
                    }

                    ApplyRequestDefaults(model, linkedRequest, budget);

                    await using var tx = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                    var remaining = await GetRemainingBudgetAsync(budget.BudgetID, id);

                    if (model.Amount > remaining || remaining < 0)
                    {
                        ModelState.AddModelError("Amount", "Expense amount exceeds the remaining allocated budget.");
                        ModelState.AddModelError("Amount",
                            $"Expense amount (₱{model.Amount:N2}) exceeds remaining budget (₱{remaining:N2}).");

                        _db.AuditLogs.Add(new AuditLog
                        {
                            UserID = CurrentUserID,
                            TenantID = tenantFilter ?? existing.TenantID,
                            LogType = "Security",
                            Severity = "Warning",
                            Action = "Budget Overrun Attempt",
                            Details = $"User '{CurrentFullName}' attempted to update expense #{id} to ₱{model.Amount:N2}. Remaining: ₱{remaining:N2}.",
                            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                        });
                        await _db.SaveChangesAsync();
                        await tx.CommitAsync();
                    }
                    else if (ModelState.IsValid)
                    {
                        var oldAmount = existing.Amount;

                        existing.BudgetID = model.BudgetID;
                        existing.BudgetRequestID = model.BudgetRequestID;
                        existing.ExpenseTitle = model.ExpenseTitle;
                        existing.Category = model.Category;
                        existing.Description = model.Description;
                        existing.Amount = model.Amount;
                        existing.ExpenseDate = model.ExpenseDate;
                        existing.DepartmentID = budget.DepartmentID;
                        existing.Year = budget.Year;

                        _db.AuditLogs.Add(new AuditLog
                        {
                            UserID = CurrentUserID,
                            TenantID = tenantFilter ?? existing.TenantID,
                            Action = "Expense Updated",
                            Details = $"Updated expense '{existing.ExpenseTitle}' (ID:{id}). Amount changed from ₱{oldAmount:N2} to ₱{model.Amount:N2}.",
                            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
                        });

                        _db.Notifications.Add(new Notification
                        {
                            TenantID = existing.TenantID,
                            Title = "Expense Updated",
                            Message = $"Expense '{existing.ExpenseTitle}' updated to ₱{model.Amount:N2}.",
                            NotificationType = "System",
                            RedirectUrl = "/Expenses"
                        });

                        await _db.SaveChangesAsync();
                        await tx.CommitAsync();
                        TempData["Success"] = "Expense updated successfully.";
                        return RedirectToAction(nameof(Index));
                    }
                }
            }

            await PopulateBudgetDropdown(model.BudgetID);
            return View(model);
        }

        // ─────────────────────────────────────────────
        // POST: Expenses/UpdateStatus
        // ─────────────────────────────────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, string newStatus)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanManage) return AccessDenied();

            var tenantFilter = GetTenantFilter();
            var expense = await _db.Expenses
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.ExpenseID == id && (tenantFilter == null || e.TenantID == tenantFilter));

            if (expense == null) return NotFound();

            var validStatuses = new[] { "Recorded", "Verified", "Archived" };
            if (!validStatuses.Contains(newStatus))
                return BadRequest("Invalid status.");

            var oldStatus = expense.Status;
            expense.Status = newStatus;

            _db.AuditLogs.Add(new AuditLog
            {
                UserID = CurrentUserID,
                TenantID = tenantFilter ?? expense.TenantID,
                Action = newStatus == "Archived" ? "Expense Archived" : "Expense Updated",
                Details = $"Expense '{expense.ExpenseTitle}' (ID:{id}) status changed from '{oldStatus}' to '{newStatus}'.",
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            });

            _db.Notifications.Add(new Notification
            {
                TenantID = expense.TenantID,
                Title = $"Expense {newStatus}",
                Message = $"Expense '{expense.ExpenseTitle}' ({expense.Department?.DepartmentName}) has been {newStatus.ToLower()}.",
                NotificationType = "System",
                RedirectUrl = "/Expenses"
            });

            await _db.SaveChangesAsync();

            TempData["Success"] = $"Expense status updated to {newStatus}.";
            return RedirectToAction(nameof(Index));
        }

        // ─────────────────────────────────────────────
        // AJAX: Get approved requests by budget
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetRequestsByBudget(int budgetId)
        {
            if (!IsAuthenticated) return Unauthorized();

            var tenantFilter = GetTenantFilter();
            var requests = await _db.BudgetRequests
                .Include(r => r.Department)
                .Include(r => r.Budget)
                .Where(r =>
                    r.BudgetID == budgetId &&
                    r.Status == "Approved" &&
                    (tenantFilter == null || r.TenantID == tenantFilter.Value))
                .Select(r => new
                {
                    r.RequestID,
                    r.Title,
                    r.Description,
                    r.RequestedAmount,
                    r.BudgetID,
                    Department = r.Department != null ? r.Department.DepartmentName : "",
                    Category = r.Budget != null ? r.Budget.Category : ""
                })
                .ToListAsync();

            return Json(requests);
        }

        [HttpGet]
        public async Task<IActionResult> GetRequestDetails(int requestId)
        {
            if (!IsAuthenticated) return Unauthorized();

            var tenantFilter = GetTenantFilter();
            var request = await _db.BudgetRequests
                .Include(r => r.Department)
                .Include(r => r.Budget)
                .FirstOrDefaultAsync(r =>
                    r.RequestID == requestId &&
                    r.Status == "Approved" &&
                    (tenantFilter == null || r.TenantID == tenantFilter.Value));

            if (request == null) return NotFound();

            return Json(new
            {
                request.RequestID,
                request.Title,
                request.Description,
                request.RequestedAmount,
                request.BudgetID,
                Department = request.Department?.DepartmentName ?? "",
                Category = request.Budget?.Category ?? ""
            });
        }

        // ─────────────────────────────────────────────
        // AJAX: Get budget details
        // ─────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetBudgetDetails(int budgetId, int? excludeExpenseId = null)
        {
            if (!IsAuthenticated) return Unauthorized();

            var tenantFilter = GetTenantFilter();
            var budget = await _db.Budgets
                .Include(b => b.Department)
                .FirstOrDefaultAsync(b =>
                    b.BudgetID == budgetId &&
                    (tenantFilter == null || b.TenantID == tenantFilter.Value) &&
                    b.Status == "Active");
            if (budget == null) return NotFound();

            var expenseQuery = _db.Expenses.Where(e => e.BudgetID == budgetId);
            if (excludeExpenseId.HasValue)
                expenseQuery = expenseQuery.Where(e => e.ExpenseID != excludeExpenseId.Value);

            var totalExpenses = await expenseQuery.SumAsync(e => (decimal?)e.Amount) ?? 0m;

            var remaining = budget.Amount - totalExpenses;
            var utilization = budget.Amount > 0
                ? Math.Round((totalExpenses / budget.Amount) * 100m, 1)
                : 0m;
            var indicator = "healthy";
            if (remaining < 0 || utilization >= 90)
                indicator = "danger";
            else if (utilization >= 70)
                indicator = "warning";

            return Json(new
            {
                Total = budget.Amount,
                Used = totalExpenses,
                Remaining = remaining,
                Utilization = utilization,
                Indicator = indicator,
                Department = budget.Department?.DepartmentName ?? "N/A",
                Category = budget.Category,
                Year = budget.Year
            });
        }

        // ─────────────────────────────────────────────
        // Helper: Populate budget dropdown
        // ─────────────────────────────────────────────
        private async Task PopulateBudgetDropdown(int? selectedBudgetId)
        {
            var tenantFilter = GetTenantFilter();
            var budgets = await _db.Budgets
                .Include(b => b.Department)
                .Where(b => (tenantFilter == null || b.TenantID == tenantFilter) && b.Status == "Active")
                .Select(b => new
                {
                    b.BudgetID,
                    DisplayText = b.Department!.DepartmentName + " - " + b.Category + " (" + b.Year + ")"
                })
                .ToListAsync();

            ViewBag.BudgetID = new SelectList(budgets, "BudgetID", "DisplayText", selectedBudgetId);
        }

        private async Task<decimal> GetRemainingBudgetAsync(int budgetId, int? excludeExpenseId = null)
        {
            var budgetAmount = await _db.Budgets
                .Where(b => b.BudgetID == budgetId)
                .Select(b => b.Amount)
                .FirstAsync();

            var expenseQuery = _db.Expenses.Where(e => e.BudgetID == budgetId);
            if (excludeExpenseId.HasValue)
                expenseQuery = expenseQuery.Where(e => e.ExpenseID != excludeExpenseId.Value);

            var spent = await expenseQuery.SumAsync(e => (decimal?)e.Amount) ?? 0m;
            return budgetAmount - spent;
        }

        private async Task<BudgetRequest?> ValidateLinkedRequestAsync(int? requestId, int budgetId, int tenantId)
        {
            if (!requestId.HasValue) return null;

            return await _db.BudgetRequests
                .Include(r => r.Budget)
                .FirstOrDefaultAsync(r =>
                    r.RequestID == requestId.Value &&
                    r.BudgetID == budgetId &&
                    r.TenantID == tenantId &&
                    r.Status == "Approved");
        }

        private static void ApplyRequestDefaults(Expense model, BudgetRequest? request, Budget budget)
        {
            if (request == null) return;

            if (string.IsNullOrWhiteSpace(model.ExpenseTitle))
                model.ExpenseTitle = request.Title;

            if (string.IsNullOrWhiteSpace(model.Category))
                model.Category = budget.Category;

            if (string.IsNullOrWhiteSpace(model.Description) && !string.IsNullOrWhiteSpace(request.Description))
                model.Description = request.Description;
        }
    }
}
