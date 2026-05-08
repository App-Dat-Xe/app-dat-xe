-- Migration: Create ScheduledTrips table (EF Core model match)
-- Idempotent — safe to re-run. Applies to all 4 databases.

DECLARE @dbs TABLE (name NVARCHAR(100), region VARCHAR(10));
INSERT INTO @dbs VALUES
    ('RideHailing_North',         'North'),
    ('RideHailing_North_Replica', 'North'),
    ('RideHailing_South',         'South'),
    ('RideHailing_South_Replica', 'South');

DECLARE @db NVARCHAR(100), @region VARCHAR(10), @sql NVARCHAR(MAX);
DECLARE cur CURSOR FOR SELECT name, region FROM @dbs;
OPEN cur;
FETCH NEXT FROM cur INTO @db, @region;

WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'
        IF NOT EXISTS (SELECT 1 FROM ' + QUOTENAME(@db) + '.sys.objects
                       WHERE object_id = OBJECT_ID(''' + @db + '.dbo.ScheduledTrips'') AND type = ''U'')
        BEGIN
            CREATE TABLE ' + QUOTENAME(@db) + '.[dbo].[ScheduledTrips] (
                [ScheduledTripId]     INT            IDENTITY(1,1) NOT NULL,
                [UserID]              INT            NOT NULL,
                [PickupAddress]       NVARCHAR(500)  NOT NULL,
                [PickupLat]           FLOAT          NULL,
                [PickupLng]           FLOAT          NULL,
                [DropoffAddress]      NVARCHAR(500)  NULL,
                [DropoffLat]          FLOAT          NULL,
                [DropoffLng]          FLOAT          NULL,
                [VehicleType]         NVARCHAR(50)   NOT NULL CONSTRAINT DF_ST_VehicleType DEFAULT (N''Xe máy''),
                [ScheduledPickupTime] DATETIME2(3)   NOT NULL,
                [Status]              VARCHAR(30)    NOT NULL CONSTRAINT DF_ST_Status      DEFAULT (''Scheduled''),
                [DistanceKm]          FLOAT          NULL,
                [EstimatedFare]       DECIMAL(18,2)  NULL,
                [TripId]              INT            NULL,
                [CreatedAt]           DATETIME2(3)   NOT NULL CONSTRAINT DF_ST_CreatedAt   DEFAULT (GETUTCDATE()),
                [UpdatedAt]           DATETIME2(3)   NULL,
                [Region]              VARCHAR(50)    NOT NULL CONSTRAINT DF_ST_Region       DEFAULT (''' + @region + '''),
                CONSTRAINT PK_ScheduledTrips PRIMARY KEY CLUSTERED ([ScheduledTripId] ASC),
                CONSTRAINT FK_ST_Users FOREIGN KEY ([UserID]) REFERENCES ' + QUOTENAME(@db) + '.[dbo].[Users]([UserID]),
                CONSTRAINT FK_ST_Trips FOREIGN KEY ([TripId]) REFERENCES ' + QUOTENAME(@db) + '.[dbo].[Trips]([TripID])
            );
        END';
    EXEC sp_executesql @sql;

    FETCH NEXT FROM cur INTO @db, @region;
END

CLOSE cur;
DEALLOCATE cur;
GO
