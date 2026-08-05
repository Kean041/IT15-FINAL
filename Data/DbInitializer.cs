using FinSight.Helpers;
using FinSight.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinSight.Data
{
    public static class DbInitializer
    {
        public static async Task EnsureAuthSchemaAsync(FinSightDbContext context, ILogger logger)
        {
            // MonsterASP demo databases are sometimes created manually or partially migrated.
            // These idempotent repairs keep login/register from failing when auth support tables lag behind the model.
            logger.LogInformation("Ensuring authentication schema has required columns and tables...");
            await context.Database.ExecuteSqlRawAsync(@"
                IF OBJECT_ID('Tenants', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('Tenants', 'SubscriptionPlan') IS NULL
                        ALTER TABLE Tenants ADD SubscriptionPlan NVARCHAR(50) NULL;

                    IF COL_LENGTH('Tenants', 'StripeCustomerId') IS NULL
                        ALTER TABLE Tenants ADD StripeCustomerId NVARCHAR(100) NULL;

                    IF COL_LENGTH('Tenants', 'StripeSubscriptionId') IS NULL
                        ALTER TABLE Tenants ADD StripeSubscriptionId NVARCHAR(100) NULL;

                    IF COL_LENGTH('Tenants', 'SubscriptionStatus') IS NULL
                        ALTER TABLE Tenants ADD SubscriptionStatus NVARCHAR(50) NULL CONSTRAINT DF_Tenants_SubscriptionStatus_Repair DEFAULT ('Pending');
                END

                IF OBJECT_ID('Users', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('Users', 'DepartmentID') IS NULL
                        ALTER TABLE Users ADD DepartmentID INT NULL;

                    IF COL_LENGTH('Users', 'IsArchived') IS NULL
                        ALTER TABLE Users ADD IsArchived BIT NOT NULL CONSTRAINT DF_Users_IsArchived_Repair DEFAULT (0);

                    IF COL_LENGTH('Users', 'FailedLoginAttempts') IS NULL
                        ALTER TABLE Users ADD FailedLoginAttempts INT NOT NULL CONSTRAINT DF_Users_FailedLoginAttempts_Repair DEFAULT (0);

                    IF COL_LENGTH('Users', 'LockoutEnd') IS NULL
                        ALTER TABLE Users ADD LockoutEnd DATETIME2 NULL;

                    IF COL_LENGTH('Users', 'IsTwoFactorEnabled') IS NULL
                        ALTER TABLE Users ADD IsTwoFactorEnabled BIT NOT NULL CONSTRAINT DF_Users_IsTwoFactorEnabled_Repair DEFAULT (0);

                    IF COL_LENGTH('Users', 'TwoFactorSecretKey') IS NULL
                        ALTER TABLE Users ADD TwoFactorSecretKey NVARCHAR(500) NULL;

                    IF COL_LENGTH('Users', 'FullName') IS NOT NULL
                        UPDATE Users SET FullName = COALESCE(NULLIF(FullName, ''), Email, 'Admin User') WHERE FullName IS NULL OR FullName = '';

                    IF COL_LENGTH('Users', 'IsArchived') IS NOT NULL
                        UPDATE Users SET IsArchived = 0 WHERE IsArchived IS NULL;

                    IF COL_LENGTH('Users', 'FailedLoginAttempts') IS NOT NULL
                        UPDATE Users SET FailedLoginAttempts = 0 WHERE FailedLoginAttempts IS NULL;

                    IF COL_LENGTH('Users', 'IsTwoFactorEnabled') IS NOT NULL
                        UPDATE Users SET IsTwoFactorEnabled = 0 WHERE IsTwoFactorEnabled IS NULL;
                END

                IF OBJECT_ID('AuditLogs', 'U') IS NULL
                BEGIN
                    CREATE TABLE AuditLogs
                    (
                        AuditLogID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
                        TenantID INT NULL,
                        UserID INT NULL,
                        LogType NVARCHAR(50) NOT NULL CONSTRAINT DF_AuditLogs_LogType_Repair DEFAULT ('System'),
                        Severity NVARCHAR(50) NOT NULL CONSTRAINT DF_AuditLogs_Severity_Repair DEFAULT ('Info'),
                        [Action] NVARCHAR(200) NOT NULL CONSTRAINT DF_AuditLogs_Action_Repair DEFAULT ('Unknown'),
                        Details NVARCHAR(2000) NULL,
                        IPAddress NVARCHAR(50) NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_AuditLogs_CreatedAt_Repair DEFAULT (GETDATE())
                    );
                END

                IF OBJECT_ID('AuditLogs', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('AuditLogs', 'TenantID') IS NULL
                        ALTER TABLE AuditLogs ADD TenantID INT NULL;

                    IF COL_LENGTH('AuditLogs', 'UserID') IS NULL
                        ALTER TABLE AuditLogs ADD UserID INT NULL;

                    IF COL_LENGTH('AuditLogs', 'LogType') IS NULL
                        ALTER TABLE AuditLogs ADD LogType NVARCHAR(50) NOT NULL CONSTRAINT DF_AuditLogs_LogType_Repair DEFAULT ('System');

                    IF COL_LENGTH('AuditLogs', 'Severity') IS NULL
                        ALTER TABLE AuditLogs ADD Severity NVARCHAR(50) NOT NULL CONSTRAINT DF_AuditLogs_Severity_Repair DEFAULT ('Info');

                    IF COL_LENGTH('AuditLogs', 'Action') IS NULL
                        ALTER TABLE AuditLogs ADD [Action] NVARCHAR(200) NOT NULL CONSTRAINT DF_AuditLogs_Action_Repair DEFAULT ('Unknown');

                    IF COL_LENGTH('AuditLogs', 'Details') IS NULL
                        ALTER TABLE AuditLogs ADD Details NVARCHAR(2000) NULL;

                    IF COL_LENGTH('AuditLogs', 'IPAddress') IS NULL
                        ALTER TABLE AuditLogs ADD IPAddress NVARCHAR(50) NULL;

                    IF COL_LENGTH('AuditLogs', 'CreatedAt') IS NULL
                        ALTER TABLE AuditLogs ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_AuditLogs_CreatedAt_Repair DEFAULT (GETDATE());
                END

                IF OBJECT_ID('Notifications', 'U') IS NULL
                BEGIN
                    CREATE TABLE Notifications
                    (
                        NotificationID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Notifications PRIMARY KEY,
                        TenantID INT NULL,
                        UserID INT NULL,
                        Title NVARCHAR(150) NOT NULL CONSTRAINT DF_Notifications_Title_Repair DEFAULT ('Notification'),
                        [Message] NVARCHAR(500) NOT NULL CONSTRAINT DF_Notifications_Message_Repair DEFAULT (''),
                        NotificationType NVARCHAR(50) NOT NULL CONSTRAINT DF_Notifications_Type_Repair DEFAULT ('System'),
                        IsRead BIT NOT NULL CONSTRAINT DF_Notifications_IsRead_Repair DEFAULT (0),
                        RedirectUrl NVARCHAR(255) NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Notifications_CreatedAt_Repair DEFAULT (GETDATE())
                    );
                END

                IF OBJECT_ID('Notifications', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('Notifications', 'TenantID') IS NULL
                        ALTER TABLE Notifications ADD TenantID INT NULL;

                    IF COL_LENGTH('Notifications', 'UserID') IS NULL
                        ALTER TABLE Notifications ADD UserID INT NULL;

                    IF COL_LENGTH('Notifications', 'Title') IS NULL
                        ALTER TABLE Notifications ADD Title NVARCHAR(150) NOT NULL CONSTRAINT DF_Notifications_Title_Repair DEFAULT ('Notification');

                    IF COL_LENGTH('Notifications', 'Message') IS NULL
                        ALTER TABLE Notifications ADD [Message] NVARCHAR(500) NOT NULL CONSTRAINT DF_Notifications_Message_Repair DEFAULT ('');

                    IF COL_LENGTH('Notifications', 'NotificationType') IS NULL
                        ALTER TABLE Notifications ADD NotificationType NVARCHAR(50) NOT NULL CONSTRAINT DF_Notifications_Type_Repair DEFAULT ('System');

                    IF COL_LENGTH('Notifications', 'IsRead') IS NULL
                        ALTER TABLE Notifications ADD IsRead BIT NOT NULL CONSTRAINT DF_Notifications_IsRead_Repair DEFAULT (0);

                    IF COL_LENGTH('Notifications', 'RedirectUrl') IS NULL
                        ALTER TABLE Notifications ADD RedirectUrl NVARCHAR(255) NULL;

                    IF COL_LENGTH('Notifications', 'CreatedAt') IS NULL
                        ALTER TABLE Notifications ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Notifications_CreatedAt_Repair DEFAULT (GETDATE());
                END

                IF OBJECT_ID('UserOTPs', 'U') IS NULL AND OBJECT_ID('Users', 'U') IS NOT NULL
                BEGIN
                    CREATE TABLE UserOTPs
                    (
                        OTPID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserOTPs PRIMARY KEY,
                        UserID INT NOT NULL,
                        TenantID INT NULL,
                        OTPHash NVARCHAR(255) NOT NULL,
                        GeneratedAt DATETIME2 NOT NULL,
                        ExpiresAt DATETIME2 NOT NULL,
                        UsedAt DATETIME2 NULL,
                        IsUsed BIT NOT NULL CONSTRAINT DF_UserOTPs_IsUsed_Repair DEFAULT (0),
                        AttemptCount INT NOT NULL CONSTRAINT DF_UserOTPs_AttemptCount_Repair DEFAULT (0),
                        IsExpired BIT NOT NULL CONSTRAINT DF_UserOTPs_IsExpired_Repair DEFAULT (0),
                        CreatedByIP NVARCHAR(50) NULL
                    );
                END

                IF OBJECT_ID('UserOTPs', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('UserOTPs', 'UserID') IS NULL
                        ALTER TABLE UserOTPs ADD UserID INT NOT NULL CONSTRAINT DF_UserOTPs_UserID_Repair DEFAULT (0);

                    IF COL_LENGTH('UserOTPs', 'TenantID') IS NULL
                        ALTER TABLE UserOTPs ADD TenantID INT NULL;

                    IF COL_LENGTH('UserOTPs', 'OTPHash') IS NULL
                        ALTER TABLE UserOTPs ADD OTPHash NVARCHAR(255) NOT NULL CONSTRAINT DF_UserOTPs_OTPHash_Repair DEFAULT ('');

                    IF COL_LENGTH('UserOTPs', 'GeneratedAt') IS NULL
                        ALTER TABLE UserOTPs ADD GeneratedAt DATETIME2 NOT NULL CONSTRAINT DF_UserOTPs_GeneratedAt_Repair DEFAULT (GETDATE());

                    IF COL_LENGTH('UserOTPs', 'ExpiresAt') IS NULL
                        ALTER TABLE UserOTPs ADD ExpiresAt DATETIME2 NOT NULL CONSTRAINT DF_UserOTPs_ExpiresAt_Repair DEFAULT (GETDATE());

                    IF COL_LENGTH('UserOTPs', 'UsedAt') IS NULL
                        ALTER TABLE UserOTPs ADD UsedAt DATETIME2 NULL;

                    IF COL_LENGTH('UserOTPs', 'IsUsed') IS NULL
                        ALTER TABLE UserOTPs ADD IsUsed BIT NOT NULL CONSTRAINT DF_UserOTPs_IsUsed_Repair DEFAULT (0);

                    IF COL_LENGTH('UserOTPs', 'AttemptCount') IS NULL
                        ALTER TABLE UserOTPs ADD AttemptCount INT NOT NULL CONSTRAINT DF_UserOTPs_AttemptCount_Repair DEFAULT (0);

                    IF COL_LENGTH('UserOTPs', 'IsExpired') IS NULL
                        ALTER TABLE UserOTPs ADD IsExpired BIT NOT NULL CONSTRAINT DF_UserOTPs_IsExpired_Repair DEFAULT (0);

                    IF COL_LENGTH('UserOTPs', 'CreatedByIP') IS NULL
                        ALTER TABLE UserOTPs ADD CreatedByIP NVARCHAR(50) NULL;
                END");

            logger.LogInformation("Authentication schema repair complete.");
        }

        public static async Task EnsureExpenseSchemaAsync(FinSightDbContext context, ILogger logger)
        {
            // Repair older/manual Expenses tables that existed before the expense module
            // was completed. EF expects these columns when listing expenses.
            logger.LogInformation("Ensuring Expenses table has required module columns...");
            await context.Database.ExecuteSqlRawAsync(@"
                IF OBJECT_ID('Expenses', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('Expenses', 'CreatedBy') IS NULL
                    BEGIN
                        DECLARE @DefaultExpenseUserID INT;
                        SELECT TOP 1 @DefaultExpenseUserID = UserID
                        FROM Users
                        ORDER BY CASE WHEN Email = 'superadmin@system.com' THEN 0 ELSE 1 END, UserID;

                        ALTER TABLE Expenses ADD CreatedBy INT NOT NULL DEFAULT (0);

                        IF @DefaultExpenseUserID IS NOT NULL
                            UPDATE Expenses SET CreatedBy = @DefaultExpenseUserID WHERE CreatedBy = 0;
                    END

                    IF COL_LENGTH('Expenses', 'Year') IS NULL
                    BEGIN
                        ALTER TABLE Expenses ADD [Year] INT NOT NULL DEFAULT (YEAR(GETDATE()));

                        IF COL_LENGTH('Expenses', 'ExpenseDate') IS NOT NULL
                            UPDATE Expenses SET [Year] = YEAR(ExpenseDate);
                    END
                END");

            logger.LogInformation("Expenses schema repair complete.");

            // Ensure Scenarios table has AppliedInflation and AppliedExchangeRate columns
            logger.LogInformation("Ensuring Scenarios table has economic assumption columns...");
            await context.Database.ExecuteSqlRawAsync(@"
                IF OBJECT_ID('Scenarios', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('Scenarios', 'AppliedInflation') IS NULL
                    BEGIN
                        ALTER TABLE Scenarios ADD AppliedInflation DECIMAL(18,2) NULL;
                    END

                    IF COL_LENGTH('Scenarios', 'AppliedExchangeRate') IS NULL
                    BEGIN
                        ALTER TABLE Scenarios ADD AppliedExchangeRate DECIMAL(18,2) NULL;
                    END
                END");

            logger.LogInformation("Scenarios schema repair complete.");
        }

        public static async Task InitializeAsync(FinSightDbContext context, ILogger logger, IConfiguration configuration)
        {
            try
            {
                logger.LogInformation("Starting database seeding logic...");

                // 1. Seed Roles into the legacy Roles table if it exists
                // This prevents FK violations since RoleConstants are used in code but the DB may have a physical constraint.
                logger.LogInformation("Seeding Roles into legacy table...");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF OBJECT_ID('Roles', 'U') IS NOT NULL
                    BEGIN
                        -- Ensure roles exist for all constants in RoleConstants.cs
                        IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleID = 0) BEGIN SET IDENTITY_INSERT Roles ON; INSERT INTO Roles (RoleID, RoleName) VALUES (0, 'Super Admin'); SET IDENTITY_INSERT Roles OFF; END
                        IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleID = 1) BEGIN SET IDENTITY_INSERT Roles ON; INSERT INTO Roles (RoleID, RoleName) VALUES (1, 'Admin'); SET IDENTITY_INSERT Roles OFF; END
                        IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleID = 2) BEGIN SET IDENTITY_INSERT Roles ON; INSERT INTO Roles (RoleID, RoleName) VALUES (2, 'Finance Manager'); SET IDENTITY_INSERT Roles OFF; END
                        IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleID = 3) BEGIN SET IDENTITY_INSERT Roles ON; INSERT INTO Roles (RoleID, RoleName) VALUES (3, 'Department Head'); SET IDENTITY_INSERT Roles OFF; END
                        IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleID = 4) BEGIN SET IDENTITY_INSERT Roles ON; INSERT INTO Roles (RoleID, RoleName) VALUES (4, 'Executive'); SET IDENTITY_INSERT Roles OFF; END
                    END");

                // 2. Seed Super Admin User
                bool superAdminExists = await context.Users
                    .AnyAsync(u => u.Email == "superadmin@system.com");

                if (!superAdminExists)
                {
                    var superAdminPassword = configuration["SeedUsers:SuperAdminPassword"];
                    if (string.IsNullOrWhiteSpace(superAdminPassword))
                    {
                        logger.LogWarning("Super Admin seed skipped because SeedUsers:SuperAdminPassword is not configured.");
                    }
                    else
                    {
                        logger.LogInformation("Super Admin not found. Seeding configured Super Admin account...");

                        var superAdmin = new User
                        {
                            FullName = "Super Admin",
                            Email = "superadmin@system.com",
                            PasswordHash = PasswordHelper.HashPassword(superAdminPassword),
                            RoleID = Roles.SuperAdmin, // 0
                            TenantID = null,
                            IsArchived = false,
                            CreatedAt = DateTime.Now
                        };

                        context.Users.Add(superAdmin);
                        await context.SaveChangesAsync();
                        logger.LogInformation("Super Admin seeded successfully.");
                    }
                }
                else
                {
                    logger.LogInformation("Super Admin already exists. Skipping seed.");
                }

                await SeedConfiguredAdminAsync(context, logger, configuration);
                await EnsureExpenseSchemaAsync(context, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while seeding the database.");
            }
        }

        private static async Task SeedConfiguredAdminAsync(FinSightDbContext context, ILogger logger, IConfiguration configuration)
        {
            var adminEmail = configuration["SeedUsers:AdminEmail"];
            var adminPassword = configuration["SeedUsers:AdminPassword"];

            if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
            {
                logger.LogInformation("Configured Admin seed skipped because SeedUsers:AdminEmail or SeedUsers:AdminPassword is not configured.");
                return;
            }

            var companyName = configuration["SeedUsers:AdminCompanyName"];
            if (string.IsNullOrWhiteSpace(companyName))
            {
                companyName = "FinSight Demo";
            }

            var adminName = configuration["SeedUsers:AdminName"];
            if (string.IsNullOrWhiteSpace(adminName))
            {
                adminName = "Demo Admin";
            }

            var tenant = await context.Tenants.FirstOrDefaultAsync(t => t.CompanyName == companyName);
            if (tenant == null)
            {
                tenant = new Tenant
                {
                    CompanyName = companyName,
                    CreatedDate = DateTime.Now,
                    SubscriptionPlan = "Enterprise",
                    SubscriptionStatus = "Active"
                };

                context.Tenants.Add(tenant);
                await context.SaveChangesAsync();
            }
            else
            {
                tenant.SubscriptionPlan ??= "Enterprise";
                tenant.SubscriptionStatus = "Active";
                context.Tenants.Update(tenant);
            }

            var admin = await context.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
            if (admin == null)
            {
                admin = new User
                {
                    TenantID = tenant.TenantID,
                    FullName = adminName,
                    Email = adminEmail,
                    PasswordHash = PasswordHelper.HashPassword(adminPassword),
                    RoleID = Roles.Admin,
                    IsArchived = false,
                    FailedLoginAttempts = 0,
                    LockoutEnd = null,
                    CreatedAt = DateTime.Now
                };

                context.Users.Add(admin);
            }
            else
            {
                admin.TenantID = tenant.TenantID;
                admin.FullName = string.IsNullOrWhiteSpace(admin.FullName) ? adminName : admin.FullName;
                admin.PasswordHash = PasswordHelper.HashPassword(adminPassword);
                admin.RoleID = Roles.Admin;
                admin.IsArchived = false;
                admin.FailedLoginAttempts = 0;
                admin.LockoutEnd = null;
                admin.CreatedAt ??= DateTime.Now;
                context.Users.Update(admin);
            }

            await context.SaveChangesAsync();
            logger.LogInformation("Configured Admin account is ready for {AdminEmail}.", adminEmail);
        }
    }
}
