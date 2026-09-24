/*
    Barkfield Road — Auto-Ship & Logistics
    Migration 007: one address per customer, named subscriptions, pause-until

    ------------------------------------------------------------------------------------
    1. dbo.DeliveryLocations is removed.

    A delivery always goes to the customer's own address, so a separate location table was
    holding a second copy of Street/City/State/ZipCode that had to agree with dbo.Customers
    forever. Two live addresses in two tables is a defect waiting to be found in the field.

    What the location table did carry that Customers did not is the routing detail — access
    notes, stop duration, preferred window, coordinates — so those columns move onto the
    customer, which is now the single home for "where this person is and how to serve them".

    Delivery history is unaffected. dbo.Deliveries already snapshots the address at scheduling
    time, and that snapshot is what stops a customer moving house from rewriting where last
    month's delivery went. The location row was never doing that job.

    Reversing this is a migration, not a config change: if the store ever needs two addresses
    for one customer, the table comes back. Confirmed as "always the customer's address".

    ------------------------------------------------------------------------------------
    2. dbo.Subscriptions gains Name.

    A customer can hold several subscriptions on different cadences, so staff need to tell
    them apart before attaching an add-on to one. Nullable: blank falls back to a name derived
    from the cadence and contents ("every 4 weeks - 3 items"), composed by
    Subscription.ComposeDisplayName.

    ------------------------------------------------------------------------------------
    3. dbo.Subscriptions gains PausedUntil.

    A pause may be open-ended (NULL) or dated. This column is data, not a trigger — nothing in
    Phase 1 wakes up at midnight. Delivery generation honours it, and until that exists it is
    a visible date plus a dashboard filter.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

/* ============================================================================
   1. Routing detail moves onto the customer
   ============================================================================ */

-- Driver-facing: gate codes, "leave at side door", "dog in yard".
IF COL_LENGTH('dbo.Customers', 'AccessNotes') IS NULL
BEGIN
    ALTER TABLE dbo.Customers ADD AccessNotes NVARCHAR(1000) NULL;
END
GO

-- How long the stop takes. Sent to the routing provider as the order's service duration.
IF COL_LENGTH('dbo.Customers', 'ServiceDurationMinutes') IS NULL
BEGIN
    ALTER TABLE dbo.Customers ADD ServiceDurationMinutes INT NOT NULL
        CONSTRAINT DF_Customers_ServiceDuration DEFAULT (5);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Customers_ServiceDuration')
BEGIN
    ALTER TABLE dbo.Customers ADD CONSTRAINT CK_Customers_ServiceDuration
        CHECK (ServiceDurationMinutes BETWEEN 0 AND 480);
END
GO

IF COL_LENGTH('dbo.Customers', 'PreferredWindowStart') IS NULL
BEGIN
    ALTER TABLE dbo.Customers ADD PreferredWindowStart TIME(0) NULL;
END
GO

IF COL_LENGTH('dbo.Customers', 'PreferredWindowEnd') IS NULL
BEGIN
    ALTER TABLE dbo.Customers ADD PreferredWindowEnd TIME(0) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Customers_Window')
BEGIN
    ALTER TABLE dbo.Customers ADD CONSTRAINT CK_Customers_Window
        CHECK (PreferredWindowStart IS NULL OR PreferredWindowEnd IS NULL
               OR PreferredWindowEnd > PreferredWindowStart);
END
GO

/*  Optional. Routific geocodes from the address string, so nothing blocks on having these —
    they are stored when a provider hands them back, and become required only if routing is
    brought in-house. */
IF COL_LENGTH('dbo.Customers', 'Latitude') IS NULL
BEGIN
    ALTER TABLE dbo.Customers ADD Latitude DECIMAL(9,6) NULL;
END
GO

IF COL_LENGTH('dbo.Customers', 'Longitude') IS NULL
BEGIN
    ALTER TABLE dbo.Customers ADD Longitude DECIMAL(9,6) NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Customers_Coordinates')
BEGIN
    ALTER TABLE dbo.Customers ADD CONSTRAINT CK_Customers_Coordinates
        CHECK ((Latitude IS NULL AND Longitude IS NULL)
               OR (Latitude IS NOT NULL AND Longitude IS NOT NULL));
END
GO

/* ============================================================================
   2. Subscriptions: name and pause-until
   ============================================================================ */

-- Blank falls back to a derived name; see Subscription.ComposeDisplayName.
IF COL_LENGTH('dbo.Subscriptions', 'Name') IS NULL
BEGIN
    ALTER TABLE dbo.Subscriptions ADD Name NVARCHAR(100) NULL;
END
GO

-- NULL while paused open-endedly. Only meaningful when Status = 3 (Paused).
IF COL_LENGTH('dbo.Subscriptions', 'PausedUntil') IS NULL
BEGIN
    ALTER TABLE dbo.Subscriptions ADD PausedUntil DATE NULL;
END
GO

/*  ---------------------------------------------------------------------------------
    4. dbo.Subscriptions gains Revision: the optimistic concurrency token.

    A save rewrites the whole aggregate, so it must not land on a row another request has
    already moved on from — that save's child lists would delete rows the other request had
    just added. Every save requires the revision it read and increments it.

    Deliberately an INT rather than comparing UpdatedAt, for two reasons:

      * SqlClient sends a .NET DateTime as SqlDbType.DATETIME, whose granularity is ~3.33ms.
        A value read out of a DATETIME2(3) column and sent straight back does not compare
        equal to itself, so a timestamp check rejects every legitimate save.
      * Two saves inside the same millisecond would carry the same timestamp, and the check
        would wave a genuine conflict through. A counter cannot collide.

    ROWVERSION would also work, but it is a binary type Dapper would need mapping help for,
    and an INT the application increments is easier to reason about in a log.
    --------------------------------------------------------------------------------- */
IF COL_LENGTH('dbo.Subscriptions', 'Revision') IS NULL
BEGIN
    ALTER TABLE dbo.Subscriptions ADD Revision INT NOT NULL
        CONSTRAINT DF_Subscriptions_Revision DEFAULT (0);
END
GO

/*  The dashboard's "paused, due back" filter. Deliberately not a filtered index on
    Status = 3: at a few hundred subscriptions the scan is free, and a filtered index would
    add a QUOTED_IDENTIFIER requirement to every write on this table for no measurable gain. */

/* ============================================================================
   3. Drop the delivery location table
   ============================================================================ */

IF EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Deliveries_DeliveryLocations')
BEGIN
    ALTER TABLE dbo.Deliveries DROP CONSTRAINT FK_Deliveries_DeliveryLocations;
END
GO

IF COL_LENGTH('dbo.Deliveries', 'DeliveryLocationId') IS NOT NULL
BEGIN
    ALTER TABLE dbo.Deliveries DROP COLUMN DeliveryLocationId;
END
GO

IF OBJECT_ID('dbo.DeliveryLocations', 'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.DeliveryLocations;
END
GO
