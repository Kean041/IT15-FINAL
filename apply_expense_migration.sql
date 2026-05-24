-- =====================================================
-- Sync EF Migrations History & Apply AddExpenseModule
-- =====================================================
-- Step 1: Ensure __EFMigrationsHistory exists
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

-- Step 2: Mark all prior migrations as already applied
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260416182408_AddScenarioAndScenarioDetail')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260416182408_AddScenarioAndScenarioDetail', N'9.0.0');

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260417185342_AddUserDepartmentAndIsArchived')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260417185342_AddUserDepartmentAndIsArchived', N'9.0.0');

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260503154449_AddStripeFieldsToTenant')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260503154449_AddStripeFieldsToTenant', N'9.0.0');

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260508205023_AddAccountLockoutFields')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260508205023_AddAccountLockoutFields', N'9.0.0');

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260517113601_AddTwoFactorFields')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260517113601_AddTwoFactorFields', N'9.0.0');

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260518085137_AddTwoFactorAuthentication')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260518085137_AddTwoFactorAuthentication', N'9.0.0');

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260522011158_AddFieldsToBudgetRequests')
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion]) VALUES (N'20260522011158_AddFieldsToBudgetRequests', N'9.0.0');
GO

-- Step 3: Apply AddExpenseModule migration (idempotent)
IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260523073853_AddExpenseModule')
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Expenses]') AND [c].[name] = N'Description');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [Expenses] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [Expenses] ALTER COLUMN [Description] nvarchar(1000) NOT NULL;
END;

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260523073853_AddExpenseModule')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Expenses]') AND name = N'BudgetRequestID')
        ALTER TABLE [Expenses] ADD [BudgetRequestID] int NULL;
END;

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260523073853_AddExpenseModule')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Expenses]') AND name = N'Category')
        ALTER TABLE [Expenses] ADD [Category] nvarchar(255) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260523073853_AddExpenseModule')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Expenses]') AND name = N'ExpenseDate')
        ALTER TABLE [Expenses] ADD [ExpenseDate] datetime2 NOT NULL DEFAULT GETDATE();
END;

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260523073853_AddExpenseModule')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Expenses]') AND name = N'ExpenseTitle')
        ALTER TABLE [Expenses] ADD [ExpenseTitle] nvarchar(255) NOT NULL DEFAULT N'';
END;

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260523073853_AddExpenseModule')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Expenses]') AND name = N'Status')
        ALTER TABLE [Expenses] ADD [Status] nvarchar(50) NOT NULL DEFAULT N'Recorded';
END;

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260523073853_AddExpenseModule')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Expenses]') AND name = N'CreatedBy')
    BEGIN
        DECLARE @DefaultExpenseUserID INT;
        SELECT TOP 1 @DefaultExpenseUserID = UserID
        FROM Users
        ORDER BY CASE WHEN Email = N'superadmin@system.com' THEN 0 ELSE 1 END, UserID;

        ALTER TABLE [Expenses] ADD [CreatedBy] int NOT NULL DEFAULT 0;

        IF @DefaultExpenseUserID IS NOT NULL
            UPDATE [Expenses] SET [CreatedBy] = @DefaultExpenseUserID WHERE [CreatedBy] = 0;
    END
END;

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260523073853_AddExpenseModule')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Expenses]') AND name = N'Year')
    BEGIN
        ALTER TABLE [Expenses] ADD [Year] int NOT NULL DEFAULT YEAR(GETDATE());

        IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[Expenses]') AND name = N'ExpenseDate')
            UPDATE [Expenses] SET [Year] = YEAR([ExpenseDate]);
    END
END;

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260523073853_AddExpenseModule')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(N'[Expenses]') AND name = N'IX_Expenses_BudgetRequestID')
        CREATE INDEX [IX_Expenses_BudgetRequestID] ON [Expenses] ([BudgetRequestID]);
END;

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260523073853_AddExpenseModule')
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_Expenses_BudgetRequests_BudgetRequestID')
        ALTER TABLE [Expenses] ADD CONSTRAINT [FK_Expenses_BudgetRequests_BudgetRequestID] FOREIGN KEY ([BudgetRequestID]) REFERENCES [BudgetRequests] ([RequestID]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (SELECT * FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20260523073853_AddExpenseModule')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260523073853_AddExpenseModule', N'9.0.0');
END;
GO

PRINT 'AddExpenseModule migration applied successfully.';
GO
