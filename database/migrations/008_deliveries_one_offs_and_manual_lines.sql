/*
    Barkfield Road — Auto-Ship & Logistics
    Migration 008: one-off deliveries, manual lines, and generation idempotency

    ------------------------------------------------------------------------------------
    1. Deliveries.SubscriptionId becomes nullable.

    A customer in the system can ask for something on a day they are not scheduled, without
    touching their subscription. That is a real delivery — it has contents, it gets procured,
    it gets billed against their card, and it belongs in their history. It just has no
    subscription behind it.

    (A route-only stop for someone NOT in the system is a different thing entirely: no
    customer, no contents, nothing to charge. That is a RouteStop with no delivery, and it
    arrives with the dispatch slice.)

    ------------------------------------------------------------------------------------
    2. DeliveryLines gains a Manual source, and SourceId becomes nullable.

    Staff need to add a line to a delivery that already exists — "she called and wants a bag
    added" happens weekly, and once the delivery row exists a subscription add-on cannot
    target it, because an add-on applies to whichever delivery is next.

    A manual line came from a person, not from a SubscriptionItem, RotationGroup or
    SubscriptionAddOn, so it has nothing to point at. Rather than inventing a fake id, the
    column goes null and a CHECK keeps every other source pointing somewhere.

    ------------------------------------------------------------------------------------
    3. Deliveries.Revision: the optimistic concurrency token.

    Same reasoning as Subscriptions.Revision (migration 007). A delivery is saved as a whole
    aggregate, lines included, and two staff working the procurement list on a busy prep day
    will land on the same delivery. Not UpdatedAt: SqlClient sends a .NET DateTime as
    SqlDbType.DATETIME, whose ~3.33ms granularity means a value read out of a DATETIME2(3)
    column does not compare equal to itself.

    ------------------------------------------------------------------------------------
    4. Generation becomes idempotent in the database, not just in application code.

    Two staff pressing "generate" for the same date at the same moment must not produce two
    deliveries for one subscription. The application checks first, but only a unique index
    makes that a guarantee.

    Filtered twice over: one-offs (no subscription) are exempt, and a cancelled delivery does
    not block regenerating for that date, which is what makes "cancel then regenerate" work.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

/* ============================================================================
   1. One-off deliveries
   ============================================================================ */

IF EXISTS (
    SELECT 1 FROM sys.columns
     WHERE object_id = OBJECT_ID('dbo.Deliveries')
       AND name = 'SubscriptionId'
       AND is_nullable = 0)
BEGIN
    -- The foreign key survives an ALTER COLUMN; a null simply does not participate in it.
    ALTER TABLE dbo.Deliveries ALTER COLUMN SubscriptionId UNIQUEIDENTIFIER NULL;
END
GO

/* ============================================================================
   2. Manual delivery lines
   ============================================================================ */

IF EXISTS (
    SELECT 1 FROM sys.columns
     WHERE object_id = OBJECT_ID('dbo.DeliveryLines')
       AND name = 'SourceId'
       AND is_nullable = 0)
BEGIN
    ALTER TABLE dbo.DeliveryLines ALTER COLUMN SourceId UNIQUEIDENTIFIER NULL;
END
GO

-- DeliveryLineSource gains 4 Manual.
IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DeliveryLines_Source')
BEGIN
    ALTER TABLE dbo.DeliveryLines DROP CONSTRAINT CK_DeliveryLines_Source;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DeliveryLines_Source')
BEGIN
    ALTER TABLE dbo.DeliveryLines ADD CONSTRAINT CK_DeliveryLines_Source
        CHECK (Source IN (1,2,3,4));
END
GO

/*  Only a manual line may have no source. Everything else still has to say which
    SubscriptionItem, RotationGroup or SubscriptionAddOn put it in the box. */
IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_DeliveryLines_SourceId')
BEGIN
    ALTER TABLE dbo.DeliveryLines ADD CONSTRAINT CK_DeliveryLines_SourceId
        CHECK (Source = 4 OR SourceId IS NOT NULL);
END
GO

/* ============================================================================
   3. Concurrency token
   ============================================================================ */

IF COL_LENGTH('dbo.Deliveries', 'Revision') IS NULL
BEGIN
    ALTER TABLE dbo.Deliveries ADD Revision INT NOT NULL
        CONSTRAINT DF_Deliveries_Revision DEFAULT (0);
END
GO

/* ============================================================================
   4. Generation idempotency

   Status 7 is Canceled — see DeliveryStatus.
   ============================================================================ */

IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
     WHERE name = 'UX_Deliveries_SubscriptionDate'
       AND object_id = OBJECT_ID('dbo.Deliveries'))
BEGIN
    CREATE UNIQUE INDEX UX_Deliveries_SubscriptionDate
        ON dbo.Deliveries (SubscriptionId, ScheduledFor)
        WHERE SubscriptionId IS NOT NULL AND Status <> 7;
END
GO
