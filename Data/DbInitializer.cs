using FinSight.Helpers;
using FinSight.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinSight.Data
{
    public static class DbInitializer
    {
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

                await EnsureExpenseSchemaAsync(context, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while seeding the database.");
            }
        }
    }
}
