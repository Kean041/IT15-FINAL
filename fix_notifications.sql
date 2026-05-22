USE DB_BPFS;
GO

ALTER TABLE [Notifications] ALTER COLUMN [UserID] INT NULL;
ALTER TABLE [Notifications] ALTER COLUMN [TenantID] INT NULL;
PRINT 'Notifications table updated to allow NULL values for UserID and TenantID.';
GO
