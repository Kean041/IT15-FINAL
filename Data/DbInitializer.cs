using FinSight.Helpers;
using FinSight.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FinSight.Data
{
    public static class DbInitializer
    {
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

                // 2. Ensure Expenses table exists (created outside EF migrations)
                logger.LogInformation("Checking for Expenses table...");
                await context.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Expenses')
                    BEGIN
                        CREATE TABLE Expenses (
                            ExpenseID INT IDENTITY(1,1) PRIMARY KEY,
                            BudgetID INT NOT NULL,
                            DepartmentID INT NOT NULL,
                            TenantID INT NOT NULL,
                            Description NVARCHAR(255) NOT NULL,
                            Amount DECIMAL(18,2) NOT NULL,
                            Year INT NOT NULL,
                            CreatedBy INT NOT NULL,
                            CreatedAt DATETIME2 NOT NULL DEFAULT GETDATE(),
                            CONSTRAINT FK_Expenses_Budgets FOREIGN KEY (BudgetID) REFERENCES Budgets(BudgetID),
                            CONSTRAINT FK_Expenses_Departments FOREIGN KEY (DepartmentID) REFERENCES Departments(DepartmentID),
                            CONSTRAINT FK_Expenses_Tenants FOREIGN KEY (TenantID) REFERENCES Tenants(TenantID),
                            CONSTRAINT FK_Expenses_Users FOREIGN KEY (CreatedBy) REFERENCES Users(UserID)
                        );
                        PRINT 'Expenses table created successfully.';
                    END
                ");
                logger.LogInformation("Expenses table check complete.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while seeding the database.");
            }
        }
    }
}
