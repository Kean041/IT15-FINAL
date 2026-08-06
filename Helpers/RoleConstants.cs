namespace FinSight.Helpers
{
    /// <summary>
    /// Centralized role ID constants and helper methods for RBAC.
    /// </summary>
    public static class Roles
    {
        public const int SuperAdmin     = 0;
        public const int Admin          = 1;
        public const int FinanceManager = 2;
        public const int DepartmentHead = 3;
        public const int Executive      = 4;

        /// <summary>
        /// Returns a human-readable role name for the given RoleID.
        /// </summary>
        public static string GetRoleName(int? roleId)
        {
            return roleId switch
            {
                SuperAdmin     => "Super Admin",
                Admin          => "Admin",
                FinanceManager => "Finance Manager",
                DepartmentHead => "Department Head",
                Executive      => "Executive",
                _              => "Unknown"
            };
        }

        /// <summary>
        /// Returns true if the role is a system role (Super Admin, Admin).
        /// These roles cannot be assigned manually via the UI.
        /// </summary>
        public static bool IsSystemRole(int roleId)
        {
            return roleId == SuperAdmin || roleId == Admin;
        }

        /// <summary>
        /// Returns true if the role is allowed to perform write operations
        /// (Create / Edit) on financial modules (Budget, Forecast, Scenario).
        /// </summary>
        public static bool CanWriteFinancials(int roleId)
        {
            return roleId == SuperAdmin
                || roleId == Admin
                || roleId == FinanceManager;
        }

        /// <summary>
        /// Returns true if the role is allowed to delete records.
        /// Only Super Admin and Admin can delete.
        /// </summary>
        public static bool CanDelete(int roleId)
        {
            return roleId == SuperAdmin
                || roleId == Admin;
        }

        /// <summary>
        /// Returns true if the role can approve/reject workflow requests.
        /// Only Executives / Top Management can approve or reject.
        /// Admin and Super Admin can view/monitor but are not primary approvers.
        /// </summary>
        public static bool CanApprove(int roleId)
        {
            return roleId == Executive;
        }

        /// <summary>
        /// Returns true if the role can manage (Create/Edit/Delete) budget allocations.
        /// Only Super Admin and Admin can manage allocations.
        /// </summary>
        public static bool CanManageAllocations(int roleId)
        {
            return roleId == SuperAdmin
                || roleId == Admin;
        }

        /// <summary>
        /// Returns true if the role can submit budget requests.
        /// Only Department Heads can submit.
        /// </summary>
        public static bool CanSubmitBudgetRequest(int roleId)
        {
            return roleId == DepartmentHead;
        }

        /// <summary>
        /// Returns true if the role mandates Two-Factor Authentication.
        /// Tenant Admin accounts require OTP; other roles can sign in normally.
        /// </summary>
        public static bool RequiresTwoFactor(int roleId)
        {
            return roleId == Admin;
        }

        /// <summary>
        /// Returns true if the role can access Financial Forecasting.
        /// Only Finance Manager (+ SuperAdmin/Admin for system oversight) can perform forecasting.
        /// </summary>
        public static bool CanAccessForecasting(int roleId)
        {
            return roleId == SuperAdmin
                || roleId == Admin
                || roleId == FinanceManager;
        }

        /// <summary>
        /// Returns true if the role can access Variance Analysis.
        /// Only Finance Manager (+ SuperAdmin/Admin for system oversight) can perform analysis.
        /// </summary>
        public static bool CanAccessAnalysis(int roleId)
        {
            return roleId == SuperAdmin
                || roleId == Admin
                || roleId == FinanceManager;
        }

        /// <summary>
        /// Returns true if the role can participate in Scenario Planning.
        /// Finance Manager prepares scenarios; Department Heads participate.
        /// </summary>
        public static bool CanAccessScenario(int roleId)
        {
            return roleId == SuperAdmin
                || roleId == Admin
                || roleId == FinanceManager
                || roleId == DepartmentHead;
        }

        /// <summary>
        /// Returns true if the role can create/edit official expense records.
        /// Only Finance Managers, Admins, and Super Admins can manage expenses.
        /// </summary>
        public static bool CanManageExpenses(int roleId)
        {
            return roleId == SuperAdmin
                || roleId == Admin
                || roleId == FinanceManager;
        }

        /// <summary>
        /// Returns true if the role can access the Reports module.
        /// All roles have at least partial access; DeptHead sees only their department data.
        /// </summary>
        public static bool CanAccessReports(int roleId)
        {
            return roleId == SuperAdmin
                || roleId == Admin
                || roleId == FinanceManager
                || roleId == Executive
                || roleId == DepartmentHead;
        }
    }
}
