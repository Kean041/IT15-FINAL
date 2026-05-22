using System;
using System.Collections.Generic;

namespace FinSight.Models.ViewModels
{
    public class AuditLogViewModel
    {
        // ── KPI Summary ──
        public int TotalLogs { get; set; }
        public int SystemLogs { get; set; }
        public int SecurityLogs { get; set; }
        public int CriticalLogs { get; set; }

        // ── Log Items ──
        public List<AuditLogItem> Logs { get; set; } = new();

        // ── Filters ──
        public string? LogTypeFilter { get; set; }
        public string? SeverityFilter { get; set; }
        public string? SearchQuery { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        // ── Pagination ──
        public int CurrentPage { get; set; } = 1;
        public int TotalPages { get; set; }
        public int PageSize { get; set; } = 20;
    }

    public class AuditLogItem
    {
        public int AuditLogID { get; set; }
        public string LogType { get; set; } = string.Empty;
        public string Severity { get; set; } = "Info";
        public string Action { get; set; } = string.Empty;
        public string? Details { get; set; }
        public string? IPAddress { get; set; }
        public DateTime CreatedAt { get; set; }

        // Joined data
        public string UserName { get; set; } = "System";
        public string CompanyName { get; set; } = "—";
    }

    public class AuditLogDetailItem
    {
        public int AuditLogID { get; set; }
        public string LogType { get; set; } = string.Empty;
        public string Severity { get; set; } = "Info";
        public string Action { get; set; } = string.Empty;
        public string? Details { get; set; }
        public string? IPAddress { get; set; }
        public DateTime CreatedAt { get; set; }
        public string UserName { get; set; } = "System";
        public string UserEmail { get; set; } = "—";
        public string CompanyName { get; set; } = "—";
    }
}
