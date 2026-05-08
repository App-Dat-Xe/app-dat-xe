-- Migration: Add VehicleType, DistanceKm, Price (fare) columns + text location columns to Trips
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
    -- VehicleType
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''VehicleType'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [VehicleType] NVARCHAR(50) NULL;';
    EXEC sp_executesql @sql;

    -- DistanceKm
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''DistanceKm'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [DistanceKm] FLOAT NULL;';
    EXEC sp_executesql @sql;

    -- Price (unified fare column name)
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''Price'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [Price] DECIMAL(18,2) NULL;';
    EXEC sp_executesql @sql;

    -- PickupLocation text (for admin/dispatcher compat)
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''PickupLocation'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [PickupLocation] NVARCHAR(500) NULL;';
    EXEC sp_executesql @sql;

    -- DropoffLocation text
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''DropoffLocation'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [DropoffLocation] NVARCHAR(500) NULL;';
    EXEC sp_executesql @sql;

    -- AcceptedAt / StartedAt / CompletedAt timestamps
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''AcceptedAt'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [AcceptedAt] DATETIME NULL;';
    EXEC sp_executesql @sql;

    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''StartedAt'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [StartedAt] DATETIME NULL;';
    EXEC sp_executesql @sql;

    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.columns
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.Trips'') AND name = ''CompletedAt'')
            ALTER TABLE ' + QUOTENAME(@db) + '.[dbo].[Trips] ADD [CompletedAt] DATETIME NULL;';
    EXEC sp_executesql @sql;

    FETCH NEXT FROM cur INTO @db;
END

CLOSE cur;
DEALLOCATE cur;
GO
