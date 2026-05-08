-- Migration: Add IsLocked to Users/Drivers, Email/IsEmailVerified to Users,
--            GPS/AverageRating to Drivers, Locations table, Ratings table,
--            Wallets table, Transactions table.
-- Idempotent — safe to re-run. Applies to all 4 databases.

DECLARE @dbs TABLE (name NVARCHAR(100));
INSERT INTO @dbs VALUES
    ('RideHailing_North'),
    ('RideHailing_North_Replica'),
    ('RideHailing_South'),
    ('RideHailing_South_Replica');

DECLARE @db NVARCHAR(100), @sql NVARCHAR(MAX);
DECLARE cur CURSOR FOR SELECT name FROM @dbs;
OPEN cur;
FETCH NEXT FROM cur INTO @db;

WHILE @@FETCH_STATUS = 0
BEGIN
    -- Users.IsLocked
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Users'') AND name = ''IsLocked'')
        BEGIN
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Users] ADD [IsLocked] BIT NOT NULL CONSTRAINT DF_Users_IsLocked DEFAULT (0);
        END';
    EXEC sp_executesql @sql;

    -- Users.Email
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Users'') AND name = ''Email'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Users] ADD [Email] NVARCHAR(255) NULL;';
    EXEC sp_executesql @sql;

    -- Users.IsEmailVerified
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Users'') AND name = ''IsEmailVerified'')
        BEGIN
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Users] ADD [IsEmailVerified] BIT NOT NULL CONSTRAINT DF_Users_IsEmailVerified DEFAULT (0);
        END';
    EXEC sp_executesql @sql;

    -- Drivers.IsLocked
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Drivers'') AND name = ''IsLocked'')
        BEGIN
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Drivers] ADD [IsLocked] BIT NOT NULL CONSTRAINT DF_Drivers_IsLocked DEFAULT (0);
        END';
    EXEC sp_executesql @sql;

    -- Drivers.IsOnline
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Drivers'') AND name = ''IsOnline'')
        BEGIN
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Drivers] ADD [IsOnline] BIT NULL CONSTRAINT DF_Drivers_IsOnline DEFAULT (0);
        END';
    EXEC sp_executesql @sql;

    -- Drivers.CurrentLat / CurrentLng
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Drivers'') AND name = ''CurrentLat'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Drivers] ADD [CurrentLat] FLOAT NULL, [CurrentLng] FLOAT NULL;';
    EXEC sp_executesql @sql;

    -- Drivers.AverageRating
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Drivers'') AND name = ''AverageRating'')
        BEGIN
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Drivers] ADD [AverageRating] FLOAT NULL CONSTRAINT DF_Drivers_Rating DEFAULT (5.0);
        END';
    EXEC sp_executesql @sql;

    -- Locations table
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.objects WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Locations'') AND type = ''U'')
        BEGIN
            CREATE TABLE ' + QUOTENAME(@db) + '.[dbo].[Locations] (
                [LocationID]   INT            IDENTITY(1,1) NOT NULL,
                [LocationName] NVARCHAR(255)  NOT NULL,
                [Address]      NVARCHAR(500)  NULL,
                [Latitude]     FLOAT          NOT NULL,
                [Longitude]    FLOAT          NOT NULL,
                CONSTRAINT PK_Locations PRIMARY KEY CLUSTERED ([LocationID] ASC)
            );
        END';
    EXEC sp_executesql @sql;

    -- Ratings table
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.objects WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Ratings'') AND type = ''U'')
        BEGIN
            CREATE TABLE ' + QUOTENAME(@db) + '.[dbo].[Ratings] (
                [RatingID]  INT            IDENTITY(1,1) NOT NULL,
                [TripID]    INT            NOT NULL,
                [UserID]    INT            NOT NULL,
                [Score]     TINYINT        NOT NULL,
                [Comment]   NVARCHAR(500)  NULL,
                [CreatedAt] DATETIME       NULL CONSTRAINT DF_Ratings_CreatedAt DEFAULT (GETDATE()),
                CONSTRAINT PK_Ratings     PRIMARY KEY CLUSTERED ([RatingID] ASC),
                CONSTRAINT UQ_Ratings_TripID UNIQUE ([TripID]),
                CONSTRAINT FK_Ratings_Trips FOREIGN KEY ([TripID]) REFERENCES ' + QUOTENAME(@db) + '.[dbo].[Trips]([TripID]),
                CONSTRAINT FK_Ratings_Users FOREIGN KEY ([UserID]) REFERENCES ' + QUOTENAME(@db) + '.[dbo].[Users]([UserID]),
                CONSTRAINT CK_Ratings_Score CHECK ([Score] BETWEEN 1 AND 5)
            );
        END';
    EXEC sp_executesql @sql;

    -- Wallets table
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.objects WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Wallets'') AND type = ''U'')
        BEGIN
            CREATE TABLE ' + QUOTENAME(@db) + '.[dbo].[Wallets] (
                [WalletID]  INT           IDENTITY(1,1) NOT NULL,
                [UserID]    INT           NOT NULL,
                [Balance]   DECIMAL(18,2) NOT NULL CONSTRAINT DF_Wallets_Balance   DEFAULT (0.0),
                [UpdatedAt] DATETIME      NULL         CONSTRAINT DF_Wallets_UpdatedAt DEFAULT (GETDATE()),
                CONSTRAINT PK_Wallets        PRIMARY KEY CLUSTERED ([WalletID] ASC),
                CONSTRAINT UQ_Wallets_UserID UNIQUE ([UserID]),
                CONSTRAINT FK_Wallets_Users  FOREIGN KEY ([UserID]) REFERENCES ' + QUOTENAME(@db) + '.[dbo].[Users]([UserID])
            );
        END';
    EXEC sp_executesql @sql;

    -- Transactions table
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.objects WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Transactions'') AND type = ''U'')
        BEGIN
            CREATE TABLE ' + QUOTENAME(@db) + '.[dbo].[Transactions] (
                [TransactionID]   INT           IDENTITY(1,1) NOT NULL,
                [WalletID]        INT           NOT NULL,
                [TripID]          INT           NULL,
                [Amount]          DECIMAL(18,2) NOT NULL,
                [TransactionType] VARCHAR(50)   NOT NULL,
                [PaymentMethod]   VARCHAR(50)   NOT NULL,
                [Status]          VARCHAR(20)   NOT NULL CONSTRAINT DF_Tx_Status    DEFAULT (''Success''),
                [CreatedAt]       DATETIME      NULL     CONSTRAINT DF_Tx_CreatedAt DEFAULT (GETDATE()),
                CONSTRAINT PK_Transactions PRIMARY KEY CLUSTERED ([TransactionID] ASC),
                CONSTRAINT FK_Tx_Wallets FOREIGN KEY ([WalletID]) REFERENCES ' + QUOTENAME(@db) + '.[dbo].[Wallets]([WalletID]),
                CONSTRAINT FK_Tx_Trips   FOREIGN KEY ([TripID])   REFERENCES ' + QUOTENAME(@db) + '.[dbo].[Trips]([TripID])
            );
        END';
    EXEC sp_executesql @sql;

    FETCH NEXT FROM cur INTO @db;
END

CLOSE cur;
DEALLOCATE cur;
GO
