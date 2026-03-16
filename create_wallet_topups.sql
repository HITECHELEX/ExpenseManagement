IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'WalletTopUps')
BEGIN
    CREATE TABLE [WalletTopUps] (
        [Id]           int            NOT NULL IDENTITY,
        [EmployeeId]   int            NOT NULL,
        [TopUpById]    int            NOT NULL,
        [Amount]       decimal(18,2)  NOT NULL,
        [Purpose]      nvarchar(max)  NOT NULL,
        [TopUpOn]      datetime2      NOT NULL,
        [BalanceAfter] decimal(18,2)  NOT NULL,
        CONSTRAINT [PK_WalletTopUps] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WalletTopUps_Users_EmployeeId] FOREIGN KEY ([EmployeeId]) REFERENCES [Users]([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_WalletTopUps_Users_TopUpById]  FOREIGN KEY ([TopUpById])  REFERENCES [Users]([Id])
    );
    PRINT 'WalletTopUps table created successfully.';
END
ELSE
    PRINT 'WalletTopUps table already exists.';
