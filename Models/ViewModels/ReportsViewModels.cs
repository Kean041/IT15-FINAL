using System;
using System.Collections.Generic;
using FinSight.Models;

namespace FinSight.Models.ViewModels
{
    public class FinancialSummaryReportViewModel
    {
        public int Year { get; set; }
        public decimal TotalBudget { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal RemainingBudget => TotalBudget - TotalExpenses;
        public decimal TotalPendingRequests { get; set; }
        public List<DepartmentSummary> DepartmentSummaries { get; set; } = new();
    }

    public class DepartmentSummary
    {
        public string DepartmentName { get; set; } = string.Empty;
        public decimal Budget { get; set; }
        public decimal Expenses { get; set; }
        public decimal PendingRequests { get; set; }
        public decimal UtilizationPercentage => Budget > 0 ? (Expenses / Budget) * 100 : 0;
    }

    public class BudgetAllocationReportViewModel
    {
        public int Year { get; set; }
        public List<Budget> Budgets { get; set; } = new();
    }

    public class BudgetRequestsReportViewModel
    {
        public int Year { get; set; }
        public string StatusFilter { get; set; } = string.Empty;
        public List<BudgetRequest> Requests { get; set; } = new();
    }

    public class ForecastingReportViewModel
    {
        public int Year { get; set; }
        public List<Forecast> Forecasts { get; set; } = new();
    }

    public class ExpensesReportViewModel
    {
        public int Year { get; set; }
        public List<Expense> Expenses { get; set; } = new();
        public decimal TotalActualSpending { get; set; }
        public List<DepartmentExpenseSummary> DepartmentExpenses { get; set; } = new();
    }

    public class DepartmentExpenseSummary
    {
        public string DepartmentName { get; set; } = string.Empty;
        public decimal TotalExpenses { get; set; }
    }

    public class VarianceReportViewModel
    {
        public int Year { get; set; }
        public List<VarianceItem> Variances { get; set; } = new();
    }

    public class VarianceItem
    {
        public string Category { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public decimal BudgetedAmount { get; set; }
        public decimal ActualAmount { get; set; }
        public decimal ForecastedAmount { get; set; }
        public decimal VarianceAmount => BudgetedAmount - ActualAmount;
        public decimal VariancePercentage => BudgetedAmount > 0 ? (VarianceAmount / BudgetedAmount) * 100 : 0;
    }

    public class AuditLogsReportViewModel
    {
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<AuditLog> Logs { get; set; } = new();
    }
}
