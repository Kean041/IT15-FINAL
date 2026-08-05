using FinSight.Data;
using FinSight.Models;
using FinSight.Models.ViewModels;
using FinSight.Services;
using FinSight.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace FinSight.Controllers
{
    public class ReportsController : BaseController
    {
        private readonly FinSightDbContext _context;

        public ReportsController(FinSightDbContext context)
        {
            _context = context;
        }

        public IActionResult Index()
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanAccessReports) return AccessDenied();

            return View();
        }

        private IQueryable<T> ApplyTenantFilter<T>(IQueryable<T> query) where T : class
        {
            var tenantId = GetTenantFilter();
            if (tenantId == null) return query; // SuperAdmin sees all
            
            // This is a generic way to filter by TenantID using reflection
            return query.Where(e => EF.Property<int>(e, "TenantID") == tenantId);
        }

        // -------------------------------------------------------------
        // FINANCIAL SUMMARY REPORT
        // -------------------------------------------------------------
        public async Task<IActionResult> FinancialSummary(int? year, string export)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanAccessReports) return AccessDenied();

            int selectedYear = year ?? DateTime.Now.Year;

            var budgetsQuery = ApplyTenantFilter(_context.Budgets)
                .AsNoTracking()
                .Where(b => b.Year == selectedYear);

            var expensesQuery = ApplyTenantFilter(_context.Expenses)
                .AsNoTracking()
                .Where(e => e.ExpenseDate.Year == selectedYear);

            var requestsQuery = ApplyTenantFilter(_context.BudgetRequests)
                .AsNoTracking()
                .Where(r => r.DateNeeded.Year == selectedYear && r.Status == "Pending");

            if (IsDeptHead)
            {
                budgetsQuery = budgetsQuery.Where(b => b.DepartmentID == CurrentDepartmentID);
                expensesQuery = expensesQuery.Where(e => e.DepartmentID == CurrentDepartmentID);
                requestsQuery = requestsQuery.Where(r => r.DepartmentID == CurrentDepartmentID);
            }

            var budgets = await budgetsQuery
                .Select(b => new
                {
                    b.DepartmentID,
                    b.Amount,
                    DepartmentName = b.Department != null ? b.Department.DepartmentName : "N/A"
                })
                .ToListAsync();

            var expenses = await expensesQuery
                .Select(e => new
                {
                    e.DepartmentID,
                    e.Amount,
                    DepartmentName = e.Department != null ? e.Department.DepartmentName : "N/A"
                })
                .ToListAsync();

            var requests = await requestsQuery
                .Select(r => new
                {
                    r.DepartmentID,
                    r.RequestedAmount,
                    DepartmentName = r.Department != null ? r.Department.DepartmentName : "N/A"
                })
                .ToListAsync();

            var vm = new FinancialSummaryReportViewModel
            {
                Year = selectedYear,
                TotalBudget = budgets.Sum(b => b.Amount),
                TotalExpenses = expenses.Sum(e => e.Amount),
                TotalPendingRequests = requests.Sum(r => r.RequestedAmount)
            };

            var departments = budgets
                .Select(b => new { b.DepartmentID, b.DepartmentName })
                .Union(expenses.Select(e => new { e.DepartmentID, e.DepartmentName }))
                .Union(requests.Select(r => new { r.DepartmentID, r.DepartmentName }))
                .GroupBy(d => d.DepartmentID)
                .Select(g => new
                {
                    DepartmentID = g.Key,
                    DepartmentName = g.Select(d => d.DepartmentName).FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)) ?? "N/A"
                })
                .OrderBy(d => d.DepartmentName)
                .ToList();

            foreach (var department in departments)
            {
                vm.DepartmentSummaries.Add(new DepartmentSummary
                {
                    DepartmentName = department.DepartmentName,
                    Budget = budgets.Where(b => b.DepartmentID == department.DepartmentID).Sum(b => b.Amount),
                    Expenses = expenses.Where(e => e.DepartmentID == department.DepartmentID).Sum(e => e.Amount),
                    PendingRequests = requests.Where(r => r.DepartmentID == department.DepartmentID).Sum(r => r.RequestedAmount)
                });
            }

            if (export == "pdf")
            {
                var headers = new List<string> { "Department", "Total Budget", "Total Expenses", "Pending Requests", "Utilization" };
                var rows = vm.DepartmentSummaries.Select(d => new List<string>
                {
                    d.DepartmentName,
                    d.Budget.ToString("C"),
                    d.Expenses.ToString("C"),
                    d.PendingRequests.ToString("C"),
                    $"{d.UtilizationPercentage:F1}%"
                }).ToList();

                // Add totals row
                rows.Add(new List<string> { "TOTAL", vm.TotalBudget.ToString("C"), vm.TotalExpenses.ToString("C"), vm.TotalPendingRequests.ToString("C"), "-" });

                var pdfBytes = PdfReportGenerator.GenerateReport($"Financial Summary - {selectedYear}", GetTenantName(), CurrentFullName, headers, rows);
                return File(pdfBytes, "application/pdf", $"FinancialSummary_{selectedYear}.pdf");
            }

            return View(vm);
        }

        // -------------------------------------------------------------
        // BUDGET ALLOCATION REPORT
        // -------------------------------------------------------------
        public async Task<IActionResult> BudgetAllocation(int? year, string export)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanAccessReports) return AccessDenied();

            int selectedYear = year ?? DateTime.Now.Year;

            var query = ApplyTenantFilter(_context.Budgets)
                .Include(b => b.Department)
                .Where(b => b.Year == selectedYear);

            if (IsDeptHead) query = query.Where(b => b.DepartmentID == CurrentDepartmentID);

            var list = await query.OrderBy(b => b.Department.DepartmentName).ToListAsync();
            var vm = new BudgetAllocationReportViewModel { Year = selectedYear, Budgets = list };

            if (export == "pdf")
            {
                var headers = new List<string> { "Department", "Category", "Amount", "Status", "Date Created" };
                var rows = list.Select(b => new List<string>
                {
                    b.Department?.DepartmentName ?? "-",
                    b.Category,
                    b.Amount.ToString("C"),
                    b.Status,
                    b.CreatedAt.ToString("MMM dd, yyyy")
                }).ToList();

                var pdfBytes = PdfReportGenerator.GenerateReport($"Budget Allocations - {selectedYear}", GetTenantName(), CurrentFullName, headers, rows);
                return File(pdfBytes, "application/pdf", $"BudgetAllocations_{selectedYear}.pdf");
            }

            return View(vm);
        }

        // -------------------------------------------------------------
        // BUDGET REQUESTS REPORT
        // -------------------------------------------------------------
        public async Task<IActionResult> BudgetRequests(int? year, string status, string export)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanAccessReports) return AccessDenied();

            int selectedYear = year ?? DateTime.Now.Year;

            var query = ApplyTenantFilter(_context.BudgetRequests)
                .Include(r => r.Department)
                .Include(r => r.Submitter)
                .Where(r => r.DateNeeded.Year == selectedYear);

            if (!string.IsNullOrEmpty(status)) query = query.Where(r => r.Status == status);
            if (IsDeptHead) query = query.Where(r => r.DepartmentID == CurrentDepartmentID);

            var list = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();
            var vm = new BudgetRequestsReportViewModel { Year = selectedYear, StatusFilter = status, Requests = list };

            if (export == "pdf")
            {
                var headers = new List<string> { "Department", "Title", "Requested By", "Amount", "Needed By", "Status" };
                var rows = list.Select(r => new List<string>
                {
                    r.Department?.DepartmentName ?? "-",
                    r.Title,
                    r.Submitter?.FullName ?? "-",
                    r.RequestedAmount.ToString("C"),
                    r.DateNeeded.ToString("MMM dd, yyyy"),
                    r.Status
                }).ToList();

                var pdfBytes = PdfReportGenerator.GenerateReport($"Budget Requests - {selectedYear}", GetTenantName(), CurrentFullName, headers, rows);
                return File(pdfBytes, "application/pdf", $"BudgetRequests_{selectedYear}.pdf");
            }

            return View(vm);
        }

        // -------------------------------------------------------------
        // FORECASTING REPORT
        // -------------------------------------------------------------
        public async Task<IActionResult> Forecasting(int? year, string export)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanAccessReports) return AccessDenied();

            int selectedYear = year ?? DateTime.Now.Year;

            var query = ApplyTenantFilter(_context.Forecasts)
                .Include(f => f.Department)
                .Where(f => f.Year == selectedYear);

            if (IsDeptHead) query = query.Where(f => f.DepartmentID == CurrentDepartmentID);

            var list = await query.OrderBy(f => f.Department.DepartmentName).ThenBy(f => f.ForecastType).ToListAsync();
            var vm = new ForecastingReportViewModel { Year = selectedYear, Forecasts = list };

            if (export == "pdf")
            {
                var headers = new List<string> { "Department", "Forecast Type", "Predicted Amount", "Year" };
                var rows = list.Select(f => new List<string>
                {
                    f.Department?.DepartmentName ?? "-",
                    f.ForecastType,
                    f.PredictedAmount.ToString("C"),
                    f.Year.ToString()
                }).ToList();

                var pdfBytes = PdfReportGenerator.GenerateReport($"Financial Forecasting - {selectedYear}", GetTenantName(), CurrentFullName, headers, rows);
                return File(pdfBytes, "application/pdf", $"Forecasting_{selectedYear}.pdf");
            }

            return View(vm);
        }

        // -------------------------------------------------------------
        // EXPENSES REPORT
        // -------------------------------------------------------------
        public async Task<IActionResult> ExpensesReport(int? year, string export)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanAccessReports) return AccessDenied();

            int selectedYear = year ?? DateTime.Now.Year;

            var query = ApplyTenantFilter(_context.Expenses)
                .AsNoTracking()
                .Where(e => e.ExpenseDate.Year == selectedYear);

            if (IsDeptHead) query = query.Where(e => e.DepartmentID == CurrentDepartmentID);

            var expenseRows = await query
                .OrderByDescending(e => e.ExpenseDate)
                .Select(e => new
                {
                    e.ExpenseID,
                    e.BudgetID,
                    e.DepartmentID,
                    e.TenantID,
                    e.ExpenseTitle,
                    e.Amount,
                    e.ExpenseDate,
                    e.Status,
                    DepartmentName = e.Department != null ? e.Department.DepartmentName : "N/A",
                    BudgetCategory = e.Budget != null ? e.Budget.Category : "N/A"
                })
                .ToListAsync();

            var list = expenseRows.Select(e => new Expense
            {
                ExpenseID = e.ExpenseID,
                BudgetID = e.BudgetID,
                DepartmentID = e.DepartmentID,
                TenantID = e.TenantID,
                ExpenseTitle = e.ExpenseTitle ?? string.Empty,
                Amount = e.Amount,
                ExpenseDate = e.ExpenseDate,
                Status = e.Status ?? "Recorded",
                Department = new Department
                {
                    DepartmentID = e.DepartmentID,
                    DepartmentName = e.DepartmentName ?? "N/A"
                },
                Budget = new Budget
                {
                    BudgetID = e.BudgetID,
                    Category = e.BudgetCategory ?? "N/A"
                }
            }).ToList();

            var vm = new ExpensesReportViewModel
            {
                Year = selectedYear,
                Expenses = list,
                TotalActualSpending = list.Sum(e => e.Amount)
            };

            var depts = list
                .Select(e => e.Department?.DepartmentName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .OrderBy(name => name)
                .ToList();

            foreach (var d in depts)
            {
                vm.DepartmentExpenses.Add(new DepartmentExpenseSummary
                {
                    DepartmentName = d!,
                    TotalExpenses = list.Where(e => e.Department?.DepartmentName == d).Sum(e => e.Amount)
                });
            }

            if (export == "pdf")
            {
                var headers = new List<string> { "Date", "Department", "Title", "Budget Category", "Amount", "Status" };
                var rows = list.Select(e => new List<string>
                {
                    e.ExpenseDate.ToString("MMM dd, yyyy"),
                    e.Department?.DepartmentName ?? "-",
                    e.ExpenseTitle,
                    e.Budget?.Category ?? "-",
                    e.Amount.ToString("C"),
                    e.Status
                }).ToList();

                var pdfBytes = PdfReportGenerator.GenerateReport($"Actual Expenses - {selectedYear}", GetTenantName(), CurrentFullName, headers, rows);
                return File(pdfBytes, "application/pdf", $"Expenses_{selectedYear}.pdf");
            }

            return View(vm);
        }

        // -------------------------------------------------------------
        // VARIANCE REPORT
        // -------------------------------------------------------------
        public async Task<IActionResult> VarianceReport(int? year, string export)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!CanAccessReports) return AccessDenied();

            int selectedYear = year ?? DateTime.Now.Year;

            var budgets = await ApplyTenantFilter(_context.Budgets)
                .AsNoTracking()
                .Where(b => b.Year == selectedYear)
                .Select(b => new
                {
                    b.BudgetID,
                    b.DepartmentID,
                    b.Category,
                    b.Amount,
                    DepartmentName = b.Department != null ? b.Department.DepartmentName : "N/A"
                })
                .ToListAsync();
                
            var expenses = await ApplyTenantFilter(_context.Expenses)
                .AsNoTracking()
                .Where(e => e.ExpenseDate.Year == selectedYear)
                .GroupBy(e => e.BudgetID)
                .Select(g => new
                {
                    BudgetID = g.Key,
                    Total = g.Sum(e => e.Amount)
                })
                .ToDictionaryAsync(g => g.BudgetID, g => g.Total);
                
            var forecasts = await ApplyTenantFilter(_context.Forecasts)
                .AsNoTracking()
                .Where(f => f.Year == selectedYear && f.ForecastType == "Base Case")
                .GroupBy(f => f.BudgetID)
                .Select(g => new
                {
                    BudgetID = g.Key,
                    Total = g.Sum(f => f.PredictedAmount)
                })
                .ToDictionaryAsync(g => g.BudgetID, g => g.Total);

            if (IsDeptHead)
            {
                budgets = budgets.Where(b => b.DepartmentID == CurrentDepartmentID).ToList();
            }

            var vm = new VarianceReportViewModel { Year = selectedYear };

            foreach (var budget in budgets)
            {
                var actuals = expenses.GetValueOrDefault(budget.BudgetID);
                var forecast = forecasts.GetValueOrDefault(budget.BudgetID);

                vm.Variances.Add(new VarianceItem
                {
                    DepartmentName = budget.DepartmentName ?? "-",
                    Category = budget.Category,
                    BudgetedAmount = budget.Amount,
                    ActualAmount = actuals,
                    ForecastedAmount = forecast
                });
            }

            if (export == "pdf")
            {
                var headers = new List<string> { "Department", "Category", "Budget", "Actuals", "Forecast", "Variance", "%" };
                var rows = vm.Variances.OrderBy(v => v.DepartmentName).Select(v => new List<string>
                {
                    v.DepartmentName,
                    v.Category,
                    v.BudgetedAmount.ToString("C"),
                    v.ActualAmount.ToString("C"),
                    v.ForecastedAmount.ToString("C"),
                    v.VarianceAmount.ToString("C"),
                    $"{v.VariancePercentage:F1}%"
                }).ToList();

                var pdfBytes = PdfReportGenerator.GenerateReport($"Variance Analysis - {selectedYear}", GetTenantName(), CurrentFullName, headers, rows);
                return File(pdfBytes, "application/pdf", $"Variance_{selectedYear}.pdf");
            }

            return View(vm);
        }

        // -------------------------------------------------------------
        // AUDIT LOGS REPORT
        // -------------------------------------------------------------
        public async Task<IActionResult> AuditLogs(string start, string end, string export)
        {
            if (!IsAuthenticated) return RedirectToLogin();
            if (!IsSuperAdmin && !IsAdmin) return AccessDenied(); // Security restriction

            DateTime startDate = string.IsNullOrEmpty(start) ? DateTime.Now.AddDays(-30) : DateTime.Parse(start);
            DateTime endDate = string.IsNullOrEmpty(end) ? DateTime.Now : DateTime.Parse(end);
            endDate = endDate.Date.AddDays(1).AddTicks(-1); // End of day

            var query = ApplyTenantFilter(_context.AuditLogs)
                .Include(a => a.User)
                .Where(a => a.CreatedAt >= startDate && a.CreatedAt <= endDate);

            var list = await query.OrderByDescending(a => a.CreatedAt).ToListAsync();
            var vm = new AuditLogsReportViewModel { StartDate = startDate, EndDate = endDate, Logs = list };

            if (export == "pdf")
            {
                var headers = new List<string> { "Date", "User", "Action", "Module", "Severity", "IP" };
                var rows = list.Select(a => new List<string>
                {
                    a.CreatedAt.ToString("MMM dd HH:mm"),
                    a.User?.FullName ?? "System",
                    a.Action,
                    a.LogType,
                    a.Severity,
                    a.IPAddress ?? "-"
                }).ToList();

                var pdfBytes = PdfReportGenerator.GenerateReport($"Audit Logs ({startDate:MMM dd} - {endDate:MMM dd})", GetTenantName(), CurrentFullName, headers, rows);
                return File(pdfBytes, "application/pdf", $"AuditLogs_{DateTime.Now:yyyyMMdd}.pdf");
            }

            return View(vm);
        }

        private string GetTenantName()
        {
            if (IsSuperAdmin) return "All Tenants (Global)";
            var tenantId = GetTenantFilter();
            var tenant = _context.Tenants.Find(tenantId);
            return tenant?.CompanyName ?? "Unknown Tenant";
        }
    }
}
