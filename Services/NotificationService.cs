using FinSight.Data;
using FinSight.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace FinSight.Services
{
    public class NotificationService
    {
        private readonly FinSightDbContext _context;

        public NotificationService(FinSightDbContext context)
        {
            _context = context;
        }

        public async Task CreateNotificationAsync(int? tenantId, int? userId, string type, string title, string message, string? redirectUrl = null)
        {
            // Sanitize IDs: 0 should be treated as null
            if (tenantId <= 0) tenantId = null;
            if (userId <= 0) userId = null;

            var notification = new Notification
            {
                TenantID = tenantId,
                UserID = userId,
                NotificationType = type,
                Title = title,
                Message = message,
                RedirectUrl = redirectUrl,
                CreatedAt = DateTime.Now,
                IsRead = false
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync();
        }

        public async Task CreateTenantBroadcastAsync(int tenantId, string type, string title, string message, string? redirectUrl = null)
        {
            if (tenantId <= 0) return;

            var userIds = await _context.Users
                .Where(u => u.TenantID == tenantId && !u.IsArchived)
                .Select(u => u.UserID)
                .ToListAsync();

            if (!userIds.Any()) return;

            var now = DateTime.Now;
            var notifications = userIds.Select(userId => new Notification
            {
                TenantID = tenantId,
                UserID = userId,
                NotificationType = type,
                Title = title,
                Message = message,
                RedirectUrl = redirectUrl,
                CreatedAt = now,
                IsRead = false
            });

            _context.Notifications.AddRange(notifications);
            await _context.SaveChangesAsync();
        }

        public async Task<int> GetUnreadCountAsync(int? tenantId, int? userId, int roleId)
        {
            var query = _context.Notifications.AsQueryable();

            if (roleId == 0) // Super Admin (Platform-wide notifications where TenantID is null)
            {
                query = query.Where(n => n.TenantID == null);
            }
            else if (roleId == 1) // Tenant Admin (Tenant-wide notifications or specific to admin)
            {
                // Admins see notifications for their tenant where UserID is null, OR notifications specifically for their UserID
                query = query.Where(n => n.TenantID == tenantId && (n.UserID == null || n.UserID == userId));
            }
            else // Regular User (Finance Manager, Dept Head, Executive)
            {
                // Only see notifications specifically directed to them
                query = query.Where(n => n.TenantID == tenantId && n.UserID == userId);
            }

            return await query.CountAsync(n => !n.IsRead);
        }

        public async Task<List<Notification>> GetRecentNotificationsAsync(int? tenantId, int? userId, int roleId, int count = 10)
        {
            var query = _context.Notifications.AsQueryable();

            if (roleId == 0) // Super Admin
            {
                query = query.Where(n => n.TenantID == null);
            }
            else if (roleId == 1) // Admin
            {
                query = query.Where(n => n.TenantID == tenantId && (n.UserID == null || n.UserID == userId));
            }
            else // Regular User
            {
                query = query.Where(n => n.TenantID == tenantId && n.UserID == userId);
            }

            return await query
                .OrderByDescending(n => n.CreatedAt)
                .Take(count)
                .ToListAsync();
        }

        public async Task MarkAsReadAsync(int notificationId, int? tenantId, int? userId, int roleId)
        {
            var notification = await _context.Notifications.FindAsync(notificationId);
            if (notification == null) return;

            // Security check to ensure the user owns this notification
            bool canAccess = false;
            if (roleId == 0 && notification.TenantID == null) canAccess = true;
            else if (roleId == 1 && notification.TenantID == tenantId) canAccess = true;
            else if (notification.TenantID == tenantId && notification.UserID == userId) canAccess = true;

            if (canAccess)
            {
                notification.IsRead = true;
                _context.Update(notification);
                await _context.SaveChangesAsync();
            }
        }

        public async Task MarkAllAsReadAsync(int? tenantId, int? userId, int roleId)
        {
            var query = _context.Notifications.Where(n => !n.IsRead);

            if (roleId == 0) // Super Admin
            {
                query = query.Where(n => n.TenantID == null);
            }
            else if (roleId == 1) // Admin
            {
                query = query.Where(n => n.TenantID == tenantId && (n.UserID == null || n.UserID == userId));
            }
            else // Regular User
            {
                query = query.Where(n => n.TenantID == tenantId && n.UserID == userId);
            }

            var notifications = await query.ToListAsync();
            foreach (var n in notifications)
            {
                n.IsRead = true;
            }

            if (notifications.Any())
            {
                _context.UpdateRange(notifications);
                await _context.SaveChangesAsync();
            }
        }
    }
}
