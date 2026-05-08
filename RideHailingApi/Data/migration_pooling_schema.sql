-- Migration: Add pooling support columns to Trips + PickupLocationID/DropoffLocationID FKs
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
    -- Ensure Locations table exists (prerequisite for FK columns)
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.objects
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Locations'') AND type = ''U'')
        BEGIN
            CREATE TABLE ' + QUOTENAME(@db) + '.[dbo].[Locations] (
                [LocationID]   INT           IDENTITY(1,1) NOT NULL,
                [LocationName] NVARCHAR(255) NOT NULL,
                [Address]      NVARCHAR(500) NULL,
                [Latitude]     FLOAT         NOT NULL,
                [Longitude]    FLOAT         NOT NULL,
                CONSTRAINT PK_Locations PRIMARY KEY CLUSTERED ([LocationID] ASC)
            );
        END';
    EXEC sp_executesql @sql;

    -- PickupLocationID FK column
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''PickupLocationID'')
        BEGIN
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [PickupLocationID] INT NULL;
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD CONSTRAINT FK_Trips_Pickup
                FOREIGN KEY ([PickupLocationID]) REFERENCES ' + QUOTENAME(@db) + '.[dbo].[Locations]([LocationID]);
        END';
    EXEC sp_executesql @sql;

    -- DropoffLocationID FK column
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''DropoffLocationID'')
        BEGIN
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [DropoffLocationID] INT NULL;
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD CONSTRAINT FK_Trips_Dropoff
                FOREIGN KEY ([DropoffLocationID]) REFERENCES ' + QUOTENAME(@db) + '.[dbo].[Locations]([LocationID]);
        END';
    EXEC sp_executesql @sql;

    -- PooledWithTripID (self-reference FK)
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''PooledWithTripID'')
        BEGIN
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [PooledWithTripID] INT NULL;
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD CONSTRAINT FK_Trips_Pooled
                FOREIGN KEY ([PooledWithTripID]) REFERENCES ' + QUOTENAME(@db) + '.[dbo].[Trips]([TripID]);
        END';
    EXEC sp_executesql @sql;

    -- MaxPassengers
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''MaxPassengers'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [MaxPassengers] INT NULL;';
    EXEC sp_executesql @sql;

    -- CurrentPassengers
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''CurrentPassengers'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [CurrentPassengers] INT NULL;';
    EXEC sp_executesql @sql;

    -- Index on PooledWithTripID for pooling queries
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.indexes
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''IX_Trips_PooledWithTripID'')
            CREATE INDEX IX_Trips_PooledWithTripID ON ' + QUOTENAME(@db) + '.[dbo].[Trips]([PooledWithTripID]);';
    EXEC sp_executesql @sql;

    FETCH NEXT FROM cur INTO @db;
END

CLOSE cur;
DEALLOCATE cur;
GO
