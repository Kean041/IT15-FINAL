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
            // MonsterASP databases for demos are often created from an older SQL script.
            // Keep the finance module usable by repairing missing tables/columns without deleting data.
            logger.LogInformation("Ensuring finance module schema has required tables and columns...");
            await context.Database.ExecuteSqlRawAsync(@"
                DECLARE @DefaultTenantID INT = 0;
                DECLARE @DefaultUserID INT = 0;
                DECLARE @DefaultDepartmentID INT = 0;
                DECLARE @DefaultBudgetID INT = 0;

                IF OBJECT_ID('Tenants', 'U') IS NOT NULL
                BEGIN
                    SELECT TOP 1 @DefaultTenantID = TenantID
                    FROM Tenants
                    ORDER BY TenantID;
                END

                IF OBJECT_ID('Users', 'U') IS NOT NULL
                BEGIN
                    SELECT TOP 1 @DefaultUserID = UserID
                    FROM Users
                    ORDER BY UserID;
                END

                IF OBJECT_ID('Departments', 'U') IS NULL
                BEGIN
                    CREATE TABLE Departments
                    (
                        DepartmentID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
                        DepartmentName NVARCHAR(255) NOT NULL CONSTRAINT DF_Departments_DepartmentName_Repair DEFAULT ('General'),
                        TenantID INT NOT NULL CONSTRAINT DF_Departments_TenantID_Repair DEFAULT (0),
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Departments_CreatedAt_Repair DEFAULT (GETDATE())
                    );
                END

                IF OBJECT_ID('Departments', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('Departments', 'DepartmentName') IS NULL
                        ALTER TABLE Departments ADD DepartmentName NVARCHAR(255) NOT NULL CONSTRAINT DF_Departments_DepartmentName_Repair DEFAULT ('General');

                    IF COL_LENGTH('Departments', 'TenantID') IS NULL
                        ALTER TABLE Departments ADD TenantID INT NOT NULL CONSTRAINT DF_Departments_TenantID_Repair DEFAULT (0);

                    IF COL_LENGTH('Departments', 'CreatedAt') IS NULL
                        ALTER TABLE Departments ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Departments_CreatedAt_Repair DEFAULT (GETDATE());

                    IF NOT EXISTS (SELECT 1 FROM Departments) AND @DefaultTenantID > 0
                        INSERT INTO Departments (DepartmentName, TenantID, CreatedAt)
                        VALUES ('General', @DefaultTenantID, GETDATE());

                    IF @DefaultTenantID > 0
                        UPDATE Departments SET TenantID = @DefaultTenantID WHERE TenantID IS NULL OR TenantID = 0;

                    UPDATE Departments SET DepartmentName = 'General' WHERE DepartmentName IS NULL OR DepartmentName = '';
                    UPDATE Departments SET CreatedAt = GETDATE() WHERE CreatedAt IS NULL;

                    SELECT TOP 1 @DefaultDepartmentID = DepartmentID
                    FROM Departments
                    ORDER BY DepartmentID;
                END

                IF OBJECT_ID('Budgets', 'U') IS NULL
                BEGIN
                    CREATE TABLE Budgets
                    (
                        BudgetID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Budgets PRIMARY KEY,
                        DepartmentID INT NOT NULL CONSTRAINT DF_Budgets_DepartmentID_Repair DEFAULT (0),
                        TenantID INT NOT NULL CONSTRAINT DF_Budgets_TenantID_Repair DEFAULT (0),
                        Category NVARCHAR(255) NOT NULL CONSTRAINT DF_Budgets_Category_Repair DEFAULT ('General'),
                        Amount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Budgets_Amount_Repair DEFAULT (0),
                        [Year] INT NOT NULL CONSTRAINT DF_Budgets_Year_Repair DEFAULT (YEAR(GETDATE())),
                        [Status] NVARCHAR(50) NOT NULL CONSTRAINT DF_Budgets_Status_Repair DEFAULT ('Draft'),
                        CreatedBy INT NOT NULL CONSTRAINT DF_Budgets_CreatedBy_Repair DEFAULT (0),
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Budgets_CreatedAt_Repair DEFAULT (GETDATE()),
                        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_Budgets_UpdatedAt_Repair DEFAULT (GETDATE())
                    );
                END

                IF OBJECT_ID('Budgets', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('Budgets', 'DepartmentID') IS NULL
                        ALTER TABLE Budgets ADD DepartmentID INT NOT NULL CONSTRAINT DF_Budgets_DepartmentID_Repair DEFAULT (0);

                    IF COL_LENGTH('Budgets', 'TenantID') IS NULL
                        ALTER TABLE Budgets ADD TenantID INT NOT NULL CONSTRAINT DF_Budgets_TenantID_Repair DEFAULT (0);

                    IF COL_LENGTH('Budgets', 'Category') IS NULL
                        ALTER TABLE Budgets ADD Category NVARCHAR(255) NOT NULL CONSTRAINT DF_Budgets_Category_Repair DEFAULT ('General');

                    IF COL_LENGTH('Budgets', 'Amount') IS NULL
                        ALTER TABLE Budgets ADD Amount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Budgets_Amount_Repair DEFAULT (0);

                    IF COL_LENGTH('Budgets', 'Year') IS NULL
                        ALTER TABLE Budgets ADD [Year] INT NOT NULL CONSTRAINT DF_Budgets_Year_Repair DEFAULT (YEAR(GETDATE()));

                    IF COL_LENGTH('Budgets', 'Status') IS NULL
                        ALTER TABLE Budgets ADD [Status] NVARCHAR(50) NOT NULL CONSTRAINT DF_Budgets_Status_Repair DEFAULT ('Draft');

                    IF COL_LENGTH('Budgets', 'CreatedBy') IS NULL
                        ALTER TABLE Budgets ADD CreatedBy INT NOT NULL CONSTRAINT DF_Budgets_CreatedBy_Repair DEFAULT (0);

                    IF COL_LENGTH('Budgets', 'CreatedAt') IS NULL
                        ALTER TABLE Budgets ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Budgets_CreatedAt_Repair DEFAULT (GETDATE());

                    IF COL_LENGTH('Budgets', 'UpdatedAt') IS NULL
                        ALTER TABLE Budgets ADD UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_Budgets_UpdatedAt_Repair DEFAULT (GETDATE());

                    IF @DefaultTenantID > 0
                        UPDATE Budgets SET TenantID = @DefaultTenantID WHERE TenantID IS NULL OR TenantID = 0;

                    IF @DefaultDepartmentID > 0
                        UPDATE Budgets SET DepartmentID = @DefaultDepartmentID WHERE DepartmentID IS NULL OR DepartmentID = 0;

                    IF @DefaultUserID > 0
                        UPDATE Budgets SET CreatedBy = @DefaultUserID WHERE CreatedBy IS NULL OR CreatedBy = 0;

                    UPDATE Budgets SET Category = 'General' WHERE Category IS NULL OR Category = '';
                    UPDATE Budgets SET [Status] = 'Draft' WHERE [Status] IS NULL OR [Status] = '';
                    UPDATE Budgets SET Amount = 0 WHERE Amount IS NULL;
                    UPDATE Budgets SET [Year] = YEAR(GETDATE()) WHERE [Year] IS NULL OR [Year] = 0;
                    UPDATE Budgets SET CreatedAt = GETDATE() WHERE CreatedAt IS NULL;
                    UPDATE Budgets SET UpdatedAt = CreatedAt WHERE UpdatedAt IS NULL;

                    SELECT TOP 1 @DefaultBudgetID = BudgetID
                    FROM Budgets
                    ORDER BY BudgetID;
                END

                IF OBJECT_ID('BudgetRequests', 'U') IS NULL
                BEGIN
                    CREATE TABLE BudgetRequests
                    (
                        RequestID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_BudgetRequests PRIMARY KEY,
                        Title NVARCHAR(255) NOT NULL CONSTRAINT DF_BudgetRequests_Title_Repair DEFAULT ('Budget Request'),
                        [Description] NVARCHAR(1000) NULL,
                        RequestedAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_BudgetRequests_RequestedAmount_Repair DEFAULT (0),
                        DateNeeded DATETIME2 NOT NULL CONSTRAINT DF_BudgetRequests_DateNeeded_Repair DEFAULT (GETDATE()),
                        DepartmentID INT NOT NULL CONSTRAINT DF_BudgetRequests_DepartmentID_Repair DEFAULT (0),
                        TenantID INT NOT NULL CONSTRAINT DF_BudgetRequests_TenantID_Repair DEFAULT (0),
                        BudgetID INT NOT NULL CONSTRAINT DF_BudgetRequests_BudgetID_Repair DEFAULT (0),
                        SubmittedBy INT NOT NULL CONSTRAINT DF_BudgetRequests_SubmittedBy_Repair DEFAULT (0),
                        [Status] NVARCHAR(50) NOT NULL CONSTRAINT DF_BudgetRequests_Status_Repair DEFAULT ('Pending'),
                        ApprovedBy INT NULL,
                        ApprovedDate DATETIME2 NULL,
                        RejectionReason NVARCHAR(1000) NULL,
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_BudgetRequests_CreatedAt_Repair DEFAULT (GETDATE()),
                        UpdatedBy INT NULL,
                        UpdatedAt DATETIME2 NULL
                    );
                END

                IF OBJECT_ID('BudgetRequests', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('BudgetRequests', 'Title') IS NULL
                        ALTER TABLE BudgetRequests ADD Title NVARCHAR(255) NOT NULL CONSTRAINT DF_BudgetRequests_Title_Repair DEFAULT ('Budget Request');

                    IF COL_LENGTH('BudgetRequests', 'Description') IS NULL
                        ALTER TABLE BudgetRequests ADD [Description] NVARCHAR(1000) NULL;

                    IF COL_LENGTH('BudgetRequests', 'RequestedAmount') IS NULL
                        ALTER TABLE BudgetRequests ADD RequestedAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_BudgetRequests_RequestedAmount_Repair DEFAULT (0);

                    IF COL_LENGTH('BudgetRequests', 'DateNeeded') IS NULL
                        ALTER TABLE BudgetRequests ADD DateNeeded DATETIME2 NOT NULL CONSTRAINT DF_BudgetRequests_DateNeeded_Repair DEFAULT (GETDATE());

                    IF COL_LENGTH('BudgetRequests', 'DepartmentID') IS NULL
                        ALTER TABLE BudgetRequests ADD DepartmentID INT NOT NULL CONSTRAINT DF_BudgetRequests_DepartmentID_Repair DEFAULT (0);

                    IF COL_LENGTH('BudgetRequests', 'TenantID') IS NULL
                        ALTER TABLE BudgetRequests ADD TenantID INT NOT NULL CONSTRAINT DF_BudgetRequests_TenantID_Repair DEFAULT (0);

                    IF COL_LENGTH('BudgetRequests', 'BudgetID') IS NULL
                        ALTER TABLE BudgetRequests ADD BudgetID INT NOT NULL CONSTRAINT DF_BudgetRequests_BudgetID_Repair DEFAULT (0);

                    IF COL_LENGTH('BudgetRequests', 'SubmittedBy') IS NULL
                        ALTER TABLE BudgetRequests ADD SubmittedBy INT NOT NULL CONSTRAINT DF_BudgetRequests_SubmittedBy_Repair DEFAULT (0);

                    IF COL_LENGTH('BudgetRequests', 'Status') IS NULL
                        ALTER TABLE BudgetRequests ADD [Status] NVARCHAR(50) NOT NULL CONSTRAINT DF_BudgetRequests_Status_Repair DEFAULT ('Pending');

                    IF COL_LENGTH('BudgetRequests', 'ApprovedBy') IS NULL
                        ALTER TABLE BudgetRequests ADD ApprovedBy INT NULL;

                    IF COL_LENGTH('BudgetRequests', 'ApprovedDate') IS NULL
                        ALTER TABLE BudgetRequests ADD ApprovedDate DATETIME2 NULL;

                    IF COL_LENGTH('BudgetRequests', 'RejectionReason') IS NULL
                        ALTER TABLE BudgetRequests ADD RejectionReason NVARCHAR(1000) NULL;

                    IF COL_LENGTH('BudgetRequests', 'CreatedAt') IS NULL
                        ALTER TABLE BudgetRequests ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_BudgetRequests_CreatedAt_Repair DEFAULT (GETDATE());

                    IF COL_LENGTH('BudgetRequests', 'UpdatedBy') IS NULL
                        ALTER TABLE BudgetRequests ADD UpdatedBy INT NULL;

                    IF COL_LENGTH('BudgetRequests', 'UpdatedAt') IS NULL
                        ALTER TABLE BudgetRequests ADD UpdatedAt DATETIME2 NULL;

                    IF @DefaultTenantID > 0
                        UPDATE BudgetRequests SET TenantID = @DefaultTenantID WHERE TenantID IS NULL OR TenantID = 0;

                    IF @DefaultDepartmentID > 0
                        UPDATE BudgetRequests SET DepartmentID = @DefaultDepartmentID WHERE DepartmentID IS NULL OR DepartmentID = 0;

                    IF @DefaultBudgetID > 0
                        UPDATE BudgetRequests SET BudgetID = @DefaultBudgetID WHERE BudgetID IS NULL OR BudgetID = 0;

                    IF @DefaultUserID > 0
                        UPDATE BudgetRequests SET SubmittedBy = @DefaultUserID WHERE SubmittedBy IS NULL OR SubmittedBy = 0;

                    UPDATE BudgetRequests SET Title = 'Budget Request' WHERE Title IS NULL OR Title = '';
                    UPDATE BudgetRequests SET [Status] = 'Pending' WHERE [Status] IS NULL OR [Status] = '';
                    UPDATE BudgetRequests SET RequestedAmount = 0 WHERE RequestedAmount IS NULL;
                    UPDATE BudgetRequests SET DateNeeded = GETDATE() WHERE DateNeeded IS NULL;
                    UPDATE BudgetRequests SET CreatedAt = GETDATE() WHERE CreatedAt IS NULL;
                END

                IF OBJECT_ID('Expenses', 'U') IS NULL
                BEGIN
                    CREATE TABLE Expenses
                    (
                        ExpenseID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Expenses PRIMARY KEY,
                        BudgetRequestID INT NULL,
                        BudgetID INT NOT NULL CONSTRAINT DF_Expenses_BudgetID_Repair DEFAULT (0),
                        DepartmentID INT NOT NULL CONSTRAINT DF_Expenses_DepartmentID_Repair DEFAULT (0),
                        TenantID INT NOT NULL CONSTRAINT DF_Expenses_TenantID_Repair DEFAULT (0),
                        ExpenseTitle NVARCHAR(255) NOT NULL CONSTRAINT DF_Expenses_ExpenseTitle_Repair DEFAULT ('Expense'),
                        Category NVARCHAR(255) NOT NULL CONSTRAINT DF_Expenses_Category_Repair DEFAULT ('General'),
                        [Description] NVARCHAR(1000) NOT NULL CONSTRAINT DF_Expenses_Description_Repair DEFAULT (''),
                        Amount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Expenses_Amount_Repair DEFAULT (0),
                        ExpenseDate DATETIME2 NOT NULL CONSTRAINT DF_Expenses_ExpenseDate_Repair DEFAULT (GETDATE()),
                        [Status] NVARCHAR(50) NOT NULL CONSTRAINT DF_Expenses_Status_Repair DEFAULT ('Recorded'),
                        CreatedBy INT NOT NULL CONSTRAINT DF_Expenses_CreatedBy_Repair DEFAULT (0),
                        [Year] INT NOT NULL CONSTRAINT DF_Expenses_Year_Repair DEFAULT (YEAR(GETDATE())),
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Expenses_CreatedAt_Repair DEFAULT (GETDATE())
                    );
                END

                IF OBJECT_ID('Expenses', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('Expenses', 'BudgetRequestID') IS NULL
                        ALTER TABLE Expenses ADD BudgetRequestID INT NULL;

                    IF COL_LENGTH('Expenses', 'BudgetID') IS NULL
                        ALTER TABLE Expenses ADD BudgetID INT NOT NULL CONSTRAINT DF_Expenses_BudgetID_Repair DEFAULT (0);

                    IF COL_LENGTH('Expenses', 'DepartmentID') IS NULL
                        ALTER TABLE Expenses ADD DepartmentID INT NOT NULL CONSTRAINT DF_Expenses_DepartmentID_Repair DEFAULT (0);

                    IF COL_LENGTH('Expenses', 'TenantID') IS NULL
                        ALTER TABLE Expenses ADD TenantID INT NOT NULL CONSTRAINT DF_Expenses_TenantID_Repair DEFAULT (0);

                    IF COL_LENGTH('Expenses', 'ExpenseTitle') IS NULL
                        ALTER TABLE Expenses ADD ExpenseTitle NVARCHAR(255) NOT NULL CONSTRAINT DF_Expenses_ExpenseTitle_Repair DEFAULT ('Expense');

                    IF COL_LENGTH('Expenses', 'Category') IS NULL
                        ALTER TABLE Expenses ADD Category NVARCHAR(255) NOT NULL CONSTRAINT DF_Expenses_Category_Repair DEFAULT ('General');

                    IF COL_LENGTH('Expenses', 'Description') IS NULL
                        ALTER TABLE Expenses ADD [Description] NVARCHAR(1000) NOT NULL CONSTRAINT DF_Expenses_Description_Repair DEFAULT ('');

                    IF COL_LENGTH('Expenses', 'Amount') IS NULL
                        ALTER TABLE Expenses ADD Amount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Expenses_Amount_Repair DEFAULT (0);

                    IF COL_LENGTH('Expenses', 'ExpenseDate') IS NULL
                    BEGIN
                        ALTER TABLE Expenses ADD ExpenseDate DATETIME2 NOT NULL CONSTRAINT DF_Expenses_ExpenseDate_Repair DEFAULT (GETDATE());

                        IF COL_LENGTH('Expenses', 'Date') IS NOT NULL
                            EXEC(N'UPDATE Expenses SET ExpenseDate = [Date] WHERE [Date] IS NOT NULL;');
                    END

                    IF COL_LENGTH('Expenses', 'Status') IS NULL
                        ALTER TABLE Expenses ADD [Status] NVARCHAR(50) NOT NULL CONSTRAINT DF_Expenses_Status_Repair DEFAULT ('Recorded');

                    IF COL_LENGTH('Expenses', 'CreatedBy') IS NULL
                        ALTER TABLE Expenses ADD CreatedBy INT NOT NULL CONSTRAINT DF_Expenses_CreatedBy_Repair DEFAULT (0);

                    IF COL_LENGTH('Expenses', 'Year') IS NULL
                        ALTER TABLE Expenses ADD [Year] INT NOT NULL CONSTRAINT DF_Expenses_Year_Repair DEFAULT (YEAR(GETDATE()));

                    IF COL_LENGTH('Expenses', 'CreatedAt') IS NULL
                        ALTER TABLE Expenses ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Expenses_CreatedAt_Repair DEFAULT (GETDATE());

                    IF @DefaultTenantID > 0
                        UPDATE Expenses SET TenantID = @DefaultTenantID WHERE TenantID IS NULL OR TenantID = 0;

                    IF @DefaultDepartmentID > 0
                        UPDATE Expenses SET DepartmentID = @DefaultDepartmentID WHERE DepartmentID IS NULL OR DepartmentID = 0;

                    IF @DefaultBudgetID > 0
                        UPDATE Expenses SET BudgetID = @DefaultBudgetID WHERE BudgetID IS NULL OR BudgetID = 0;

                    IF @DefaultUserID > 0
                        UPDATE Expenses SET CreatedBy = @DefaultUserID WHERE CreatedBy IS NULL OR CreatedBy = 0;

                    UPDATE Expenses SET ExpenseTitle = COALESCE(NULLIF(ExpenseTitle, ''), NULLIF([Description], ''), 'Expense') WHERE ExpenseTitle IS NULL OR ExpenseTitle = '';
                    UPDATE Expenses SET Category = 'General' WHERE Category IS NULL OR Category = '';
                    UPDATE Expenses SET [Description] = '' WHERE [Description] IS NULL;
                    UPDATE Expenses SET [Status] = 'Recorded' WHERE [Status] IS NULL OR [Status] = '';
                    UPDATE Expenses SET Amount = 0 WHERE Amount IS NULL;
                    UPDATE Expenses SET ExpenseDate = GETDATE() WHERE ExpenseDate IS NULL;
                    UPDATE Expenses SET [Year] = YEAR(ExpenseDate) WHERE [Year] IS NULL OR [Year] = 0;
                    UPDATE Expenses SET CreatedAt = GETDATE() WHERE CreatedAt IS NULL;
                END

                IF OBJECT_ID('Forecasts', 'U') IS NULL
                BEGIN
                    CREATE TABLE Forecasts
                    (
                        ForecastID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Forecasts PRIMARY KEY,
                        DepartmentID INT NOT NULL CONSTRAINT DF_Forecasts_DepartmentID_Repair DEFAULT (0),
                        TenantID INT NOT NULL CONSTRAINT DF_Forecasts_TenantID_Repair DEFAULT (0),
                        BudgetID INT NOT NULL CONSTRAINT DF_Forecasts_BudgetID_Repair DEFAULT (0),
                        ForecastType NVARCHAR(50) NOT NULL CONSTRAINT DF_Forecasts_ForecastType_Repair DEFAULT ('Base Case'),
                        PredictedAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Forecasts_PredictedAmount_Repair DEFAULT (0),
                        [Year] INT NOT NULL CONSTRAINT DF_Forecasts_Year_Repair DEFAULT (YEAR(GETDATE())),
                        CreatedBy INT NOT NULL CONSTRAINT DF_Forecasts_CreatedBy_Repair DEFAULT (0),
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Forecasts_CreatedAt_Repair DEFAULT (GETDATE())
                    );
                END

                IF OBJECT_ID('Forecasts', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('Forecasts', 'DepartmentID') IS NULL
                        ALTER TABLE Forecasts ADD DepartmentID INT NOT NULL CONSTRAINT DF_Forecasts_DepartmentID_Repair DEFAULT (0);

                    IF COL_LENGTH('Forecasts', 'TenantID') IS NULL
                        ALTER TABLE Forecasts ADD TenantID INT NOT NULL CONSTRAINT DF_Forecasts_TenantID_Repair DEFAULT (0);

                    IF COL_LENGTH('Forecasts', 'BudgetID') IS NULL
                        ALTER TABLE Forecasts ADD BudgetID INT NOT NULL CONSTRAINT DF_Forecasts_BudgetID_Repair DEFAULT (0);

                    IF COL_LENGTH('Forecasts', 'ForecastType') IS NULL
                        ALTER TABLE Forecasts ADD ForecastType NVARCHAR(50) NOT NULL CONSTRAINT DF_Forecasts_ForecastType_Repair DEFAULT ('Base Case');

                    IF COL_LENGTH('Forecasts', 'PredictedAmount') IS NULL
                        ALTER TABLE Forecasts ADD PredictedAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_Forecasts_PredictedAmount_Repair DEFAULT (0);

                    IF COL_LENGTH('Forecasts', 'Year') IS NULL
                        ALTER TABLE Forecasts ADD [Year] INT NOT NULL CONSTRAINT DF_Forecasts_Year_Repair DEFAULT (YEAR(GETDATE()));

                    IF COL_LENGTH('Forecasts', 'CreatedBy') IS NULL
                        ALTER TABLE Forecasts ADD CreatedBy INT NOT NULL CONSTRAINT DF_Forecasts_CreatedBy_Repair DEFAULT (0);

                    IF COL_LENGTH('Forecasts', 'CreatedAt') IS NULL
                        ALTER TABLE Forecasts ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Forecasts_CreatedAt_Repair DEFAULT (GETDATE());

                    IF @DefaultTenantID > 0
                        UPDATE Forecasts SET TenantID = @DefaultTenantID WHERE TenantID IS NULL OR TenantID = 0;

                    IF @DefaultDepartmentID > 0
                        UPDATE Forecasts SET DepartmentID = @DefaultDepartmentID WHERE DepartmentID IS NULL OR DepartmentID = 0;

                    IF @DefaultBudgetID > 0
                        UPDATE Forecasts SET BudgetID = @DefaultBudgetID WHERE BudgetID IS NULL OR BudgetID = 0;

                    IF @DefaultUserID > 0
                        UPDATE Forecasts SET CreatedBy = @DefaultUserID WHERE CreatedBy IS NULL OR CreatedBy = 0;

                    UPDATE Forecasts SET ForecastType = 'Base Case' WHERE ForecastType IS NULL OR ForecastType = '';
                    UPDATE Forecasts SET PredictedAmount = 0 WHERE PredictedAmount IS NULL;
                    UPDATE Forecasts SET [Year] = YEAR(GETDATE()) WHERE [Year] IS NULL OR [Year] = 0;
                    UPDATE Forecasts SET CreatedAt = GETDATE() WHERE CreatedAt IS NULL;
                END

                IF OBJECT_ID('Scenarios', 'U') IS NULL
                BEGIN
                    CREATE TABLE Scenarios
                    (
                        ScenarioID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Scenarios PRIMARY KEY,
                        ScenarioName NVARCHAR(255) NOT NULL CONSTRAINT DF_Scenarios_ScenarioName_Repair DEFAULT ('Scenario'),
                        [Description] NVARCHAR(1000) NULL,
                        TenantID INT NOT NULL CONSTRAINT DF_Scenarios_TenantID_Repair DEFAULT (0),
                        CreatedBy INT NOT NULL CONSTRAINT DF_Scenarios_CreatedBy_Repair DEFAULT (0),
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Scenarios_CreatedAt_Repair DEFAULT (GETDATE()),
                        AppliedInflation DECIMAL(18,2) NULL,
                        AppliedExchangeRate DECIMAL(18,2) NULL
                    );
                END

                IF OBJECT_ID('Scenarios', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('Scenarios', 'ScenarioName') IS NULL
                        ALTER TABLE Scenarios ADD ScenarioName NVARCHAR(255) NOT NULL CONSTRAINT DF_Scenarios_ScenarioName_Repair DEFAULT ('Scenario');

                    IF COL_LENGTH('Scenarios', 'Description') IS NULL
                        ALTER TABLE Scenarios ADD [Description] NVARCHAR(1000) NULL;

                    IF COL_LENGTH('Scenarios', 'TenantID') IS NULL
                        ALTER TABLE Scenarios ADD TenantID INT NOT NULL CONSTRAINT DF_Scenarios_TenantID_Repair DEFAULT (0);

                    IF COL_LENGTH('Scenarios', 'CreatedBy') IS NULL
                        ALTER TABLE Scenarios ADD CreatedBy INT NOT NULL CONSTRAINT DF_Scenarios_CreatedBy_Repair DEFAULT (0);

                    IF COL_LENGTH('Scenarios', 'CreatedAt') IS NULL
                        ALTER TABLE Scenarios ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_Scenarios_CreatedAt_Repair DEFAULT (GETDATE());

                    IF COL_LENGTH('Scenarios', 'AppliedInflation') IS NULL
                        ALTER TABLE Scenarios ADD AppliedInflation DECIMAL(18,2) NULL;

                    IF COL_LENGTH('Scenarios', 'AppliedExchangeRate') IS NULL
                        ALTER TABLE Scenarios ADD AppliedExchangeRate DECIMAL(18,2) NULL;

                    IF @DefaultTenantID > 0
                        UPDATE Scenarios SET TenantID = @DefaultTenantID WHERE TenantID IS NULL OR TenantID = 0;

                    IF @DefaultUserID > 0
                        UPDATE Scenarios SET CreatedBy = @DefaultUserID WHERE CreatedBy IS NULL OR CreatedBy = 0;

                    UPDATE Scenarios SET ScenarioName = 'Scenario' WHERE ScenarioName IS NULL OR ScenarioName = '';
                    UPDATE Scenarios SET CreatedAt = GETDATE() WHERE CreatedAt IS NULL;
                END

                IF OBJECT_ID('ScenarioDetails', 'U') IS NULL
                BEGIN
                    CREATE TABLE ScenarioDetails
                    (
                        ScenarioDetailID INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ScenarioDetails PRIMARY KEY,
                        ScenarioID INT NOT NULL CONSTRAINT DF_ScenarioDetails_ScenarioID_Repair DEFAULT (0),
                        BudgetID INT NOT NULL CONSTRAINT DF_ScenarioDetails_BudgetID_Repair DEFAULT (0),
                        DepartmentID INT NOT NULL CONSTRAINT DF_ScenarioDetails_DepartmentID_Repair DEFAULT (0),
                        AdjustedAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_ScenarioDetails_AdjustedAmount_Repair DEFAULT (0),
                        TenantID INT NOT NULL CONSTRAINT DF_ScenarioDetails_TenantID_Repair DEFAULT (0),
                        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_ScenarioDetails_CreatedAt_Repair DEFAULT (GETDATE())
                    );
                END

                IF OBJECT_ID('ScenarioDetails', 'U') IS NOT NULL
                BEGIN
                    IF COL_LENGTH('ScenarioDetails', 'ScenarioID') IS NULL
                        ALTER TABLE ScenarioDetails ADD ScenarioID INT NOT NULL CONSTRAINT DF_ScenarioDetails_ScenarioID_Repair DEFAULT (0);

                    IF COL_LENGTH('ScenarioDetails', 'BudgetID') IS NULL
                        ALTER TABLE ScenarioDetails ADD BudgetID INT NOT NULL CONSTRAINT DF_ScenarioDetails_BudgetID_Repair DEFAULT (0);

                    IF COL_LENGTH('ScenarioDetails', 'DepartmentID') IS NULL
                        ALTER TABLE ScenarioDetails ADD DepartmentID INT NOT NULL CONSTRAINT DF_ScenarioDetails_DepartmentID_Repair DEFAULT (0);

                    IF COL_LENGTH('ScenarioDetails', 'AdjustedAmount') IS NULL
                        ALTER TABLE ScenarioDetails ADD AdjustedAmount DECIMAL(18,2) NOT NULL CONSTRAINT DF_ScenarioDetails_AdjustedAmount_Repair DEFAULT (0);

                    IF COL_LENGTH('ScenarioDetails', 'TenantID') IS NULL
                        ALTER TABLE ScenarioDetails ADD TenantID INT NOT NULL CONSTRAINT DF_ScenarioDetails_TenantID_Repair DEFAULT (0);

                    IF COL_LENGTH('ScenarioDetails', 'CreatedAt') IS NULL
                        ALTER TABLE ScenarioDetails ADD CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_ScenarioDetails_CreatedAt_Repair DEFAULT (GETDATE());

                    IF @DefaultTenantID > 0
                        UPDATE ScenarioDetails SET TenantID = @DefaultTenantID WHERE TenantID IS NULL OR TenantID = 0;

                    IF @DefaultDepartmentID > 0
                        UPDATE ScenarioDetails SET DepartmentID = @DefaultDepartmentID WHERE DepartmentID IS NULL OR DepartmentID = 0;

                    IF @DefaultBudgetID > 0
                        UPDATE ScenarioDetails SET BudgetID = @DefaultBudgetID WHERE BudgetID IS NULL OR BudgetID = 0;

                    UPDATE ScenarioDetails SET AdjustedAmount = 0 WHERE AdjustedAmount IS NULL;
                    UPDATE ScenarioDetails SET CreatedAt = GETDATE() WHERE CreatedAt IS NULL;
                END");

            logger.LogInformation("Finance module schema repair complete.");
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
                    IsTwoFactorEnabled = false,
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
                admin.IsTwoFactorEnabled = false;
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
