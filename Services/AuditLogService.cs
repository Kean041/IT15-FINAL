using FinSight.Data;
using FinSight.Models;

namespace FinSight.Services
{
    /// <summary>
    /// Centralized audit logging service. Inject this into any controller
    /// to record System or Security events.
    /// </summary>
    public class AuditLogService
    {
        private readonly FinSightDbContext _context;

        public AuditLogService(FinSightDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Logs an operational/system event (e.g. budget approved, user created).
        /// </summary>
        public async Task LogSystemAction(
            int? tenantId,
            int? userId,
            string action,
            string details,
            string? ipAddress = null,
            string severity = "Info")
        {
            await WriteLog("System", severity, tenantId, userId, action, details, ipAddress);
        }

        /// <summary>
        /// Logs a security-related event (e.g. login, failed attempt, role change).
        /// </summary>
        public async Task LogSecurityAction(
            int? tenantId,
            int? userId,
            string action,
            string details,
            string? ipAddress = null,
            string severity = "Info")
        {
            await WriteLog("Security", severity, tenantId, userId, action, details, ipAddress);
        }

        private async Task WriteLog(
            string logType,
            string severity,
            int? tenantId,
            int? userId,
            string action,
            string details,
            string? ipAddress)
        {
            // Sanitize IDs: 0 should be treated as null (Platform-wide / Super Admin)
            if (tenantId <= 0) tenantId = null;
            if (userId <= 0) userId = null;

            var log = new AuditLog
            {
                TenantID = tenantId,
                UserID = userId,
                LogType = logType,
                Severity = severity,
                Action = action,
                Details = details,
                IPAddress = ipAddress,
                CreatedAt = DateTime.Now
            };

            _context.AuditLogs.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}
