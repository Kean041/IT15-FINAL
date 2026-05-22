USE DB_BPFS;
GO

IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID(N'[Users]') AND name = 'FailedOTPAttempts')
BEGIN
    ALTER TABLE [Users] ADD 
        [FailedOTPAttempts] INT NOT NULL DEFAULT 0,
        [IsTwoFactorEnabled] BIT NOT NULL DEFAULT 0,
        [LastOTPSentAt] DATETIME2(7) NULL,
        [OTPCode] NVARCHAR(500) NULL,
        [OTPExpiration] DATETIME2(7) NULL,
        [OTPLockoutEnd] DATETIME2(7) NULL,
        [TwoFactorSecretKey] NVARCHAR(500) NULL;
    PRINT 'Added 2FA columns to Users table.';
END
ELSE
BEGIN
    PRINT '2FA columns already exist.';
END
GO
