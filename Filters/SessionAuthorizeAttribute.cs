using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace FinSight.Filters
{
    /// <summary>
    /// Enforces the app's session-based authentication model at the action/controller level.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class SessionAuthorizeAttribute : Attribute, IAuthorizationFilter
    {
        private readonly int[] _allowedRoles;

        public SessionAuthorizeAttribute(params int[] allowedRoles)
        {
            _allowedRoles = allowedRoles;
        }

        public void OnAuthorization(AuthorizationFilterContext context)
        {
            int? userId = context.HttpContext.Session.GetInt32("UserID");
            int? roleId = context.HttpContext.Session.GetInt32("RoleID");

            if (userId == null || roleId == null)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            if (_allowedRoles.Length > 0 && !_allowedRoles.Contains(roleId.Value))
            {
                context.Result = new ForbidResult();
            }
        }
    }
}
