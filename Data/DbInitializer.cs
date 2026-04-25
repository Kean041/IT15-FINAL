using FinSight.Helpers;
using FinSight.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinSight.Data
{
    public static class DbInitializer
    {
        public static async Task InitializeAsync(FinSightDbContext context, ILogger logger)
        {
            // Optional: Ensure database is created (Warning: Not ideal if using Migrations strictly in prod, but safe here)
            // await context.Database.EnsureCreatedAsync();

            try
            {
                logger.LogInformation("Starting database seeding logic...");

                // 1. Seed Super Admin User
                // Check if the system Super Admin already exists by role or email
                bool superAdminExists = await context.Users
                    .AnyAsync(u => u.RoleID == Roles.SuperAdmin || u.Email == "superadmin@system.com");

                if (!superAdminExists)
                {
                    logger.LogInformation("Super Admin not found. Seeding default Super Admin account...");

                    // WARNING: The physical database contains a legacy FK_Users_Roles constraint despite us moving to RoleConstants.
                    // We must execute a raw SQL injection strictly for Role 0 to bypass EF Core's FK collision error silently failing!
                    await context.Database.ExecuteSqlRawAsync(
                        "IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleID = 0) " +
                        "BEGIN " +
                        "   SET IDENTITY_INSERT Roles ON; " +
                        "   INSERT INTO Roles (RoleID, RoleName) VALUES (0, 'Super Admin'); " +
                        "   SET IDENTITY_INSERT Roles OFF; " +
                        "END");

                    var superAdmin = new User
                    {
                        FullName = "Super Admin",
                        Email = "superadmin@system.com",
                        PasswordHash = PasswordHelper.HashPassword("SuperAdmin123!"),
                        RoleID = Roles.SuperAdmin, // 0
                        TenantID = null, // System-level user has no tenant lock
                        IsArchived = false,
                        CreatedAt = DateTime.Now
                    };

                    context.Users.Add(superAdmin);
                    
                    // We DO NOT seed a Roles table physically because we leverage the lightweight static 'Roles' constants class (RoleConstants.cs) natively.
                    
                    await context.SaveChangesAsync();
                    logger.LogInformation("Super Admin seeded successfully.");
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
