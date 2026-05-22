using System;
using System.Collections.Generic;

namespace FinSight.Models.ViewModels
{
    /// <summary>
    /// ViewModel for the main ERP Dashboard.
    /// Aggregates KPIs, chart data, and recent activity.
    /// </summary>
    public class DashboardViewModel
    {
        // ── KPI Summary Cards ──────────────────────────
        public decimal TotalBudget { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal RemainingBudget => TotalBudget - TotalExpenses;
        public int PendingRequests { get; set; }

        // ── Budget vs Expenses (Bar Chart) ─────────────
        public List<string> BarChartLabels { get; set; } = new();
        public List<decimal> BarChartBudgetData { get; set; } = new();
        public List<decimal> BarChartExpenseData { get; set; } = new();

        // ── Monthly Expense Trend (Line Chart) ─────────
        public List<string> LineChartLabels { get; set; } = new();
        public List<decimal> LineChartData { get; set; } = new();

        // ── Recent Budget Requests ─────────────────────
        public List<RecentRequestItem> RecentRequests { get; set; } = new();

        // ── User Info ──────────────────────────────────
        public string UserName { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;

        // ── Alpha Vantage Market Data ──────────────────
        public MarketDataViewModel? MarketData { get; set; }
    }

    /// <summary>
    /// Lightweight item for the Recent Requests table.
    /// </summary>
    public class RecentRequestItem
    {
        public int RequestID { get; set; }
        public string DepartmentName { get; set; } = string.Empty;
        public decimal RequestedAmount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string SubmittedBy { get; set; } = string.Empty;
    }
}
