using FinSight.Filters;
using FinSight.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace FinSight.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [SessionAuthorize]
    public class NotificationController : ControllerBase
    {
        private readonly NotificationService _notificationService;

        public NotificationController(NotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        private (int? tenantId, int? userId, int roleId) GetSessionData()
        {
            int? tenantId = HttpContext.Session.GetInt32("TenantID");
            int? userId = HttpContext.Session.GetInt32("UserID");
            int roleId = HttpContext.Session.GetInt32("RoleID") ?? -1;
            return (tenantId, userId, roleId);
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var (tenantId, userId, roleId) = GetSessionData();
            
            // Adjust for SuperAdmin: tenantId is null in DB for platform, but session might have it as 0.
            int? effectiveTenantId = roleId == 0 ? null : tenantId;

            int count = await _notificationService.GetUnreadCountAsync(effectiveTenantId, userId, roleId);
            return Ok(new { count });
        }

        [HttpGet("list")]
        public async Task<IActionResult> GetNotifications()
        {
            var (tenantId, userId, roleId) = GetSessionData();

            int? effectiveTenantId = roleId == 0 ? null : tenantId;

            var notifications = await _notificationService.GetRecentNotificationsAsync(effectiveTenantId, userId, roleId);
            
            var result = notifications.Select(n => new
            {
                id = n.NotificationID,
                title = n.Title,
                message = n.Message,
                type = n.NotificationType,
                isRead = n.IsRead,
                url = n.RedirectUrl,
                date = n.CreatedAt.ToString("MMM dd, yyyy HH:mm")
            });

            return Ok(result);
        }

        [HttpPost("mark-read/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsRead(int id)
        {
            var (tenantId, userId, roleId) = GetSessionData();

            int? effectiveTenantId = roleId == 0 ? null : tenantId;

            await _notificationService.MarkAsReadAsync(id, effectiveTenantId, userId, roleId);
            return Ok();
        }

        [HttpPost("mark-all-read")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var (tenantId, userId, roleId) = GetSessionData();

            int? effectiveTenantId = roleId == 0 ? null : tenantId;

            await _notificationService.MarkAllAsReadAsync(effectiveTenantId, userId, roleId);
            return Ok();
        }
    }
}
