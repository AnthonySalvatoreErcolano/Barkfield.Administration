/*
    Barkfield Road — Auto-Ship & Logistics
    Migration 009: billing a delivery through Square

    ------------------------------------------------------------------------------------
    1. HasPaid becomes PaymentStatus.

    A bit can say paid or not-paid. It cannot say "we tried and the card declined", which is
    the state the dispatch screen exists to show — somebody has to go and ring that customer
    before the van leaves. So the flag is replaced by a status, the reason Square gave, and
    when it was last attempted.

    HasPaid is dropped rather than kept alongside. Two columns meaning "paid" would drift.

    ------------------------------------------------------------------------------------
    2. PaymentAttemptCount exists because of how Square's idempotency actually behaves.

    Verified against the sandbox:
      * same idempotency key + identical payload  -> returns the ORIGINAL payment, no double charge
      * same idempotency key + different payload  -> 400 IDEMPOTENCY_KEY_REUSED

    So the key protects against a double-click, but it cannot be the delivery id alone: a
    deliberate retry after a decline would replay the old result or be refused outright. The
    key is {DeliveryId}:pay:{PaymentAttemptCount}, and the counter is incremented and persisted
    before the call so a crash mid-charge cannot reuse a key with different contents.

    ------------------------------------------------------------------------------------
    3. SquareOrderId is stored before the payment is attempted.

    Also verified: CreateOrder and CreatePayment are two calls with two keys, and a failed
    payment leaves the order OPEN. Persisting the order id first means a retry reuses that one
    order instead of orphaning a new one on every decline.

    ------------------------------------------------------------------------------------
    4. dbo.DeliveryDiscounts — the discounts staff chose for this delivery.

    Several per delivery, which Square supports. Only the id is sent; Square computes the
    reduction and the total, because Square's catalog is the source of truth for pricing.

    The name and rate are snapshotted for display only, so a delivery from March still says
    what discount it got after somebody edits or deletes that discount in Square. Nothing
    calculates from them.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

/* ============================================================================
   1. Payment state
   ============================================================================ */

-- PaymentStatus: 1 NotCharged, 2 Paid, 3 Failed
IF COL_LENGTH('dbo.Deliveries', 'PaymentStatus') IS NULL
BEGIN
    ALTER TABLE dbo.Deliveries ADD PaymentStatus TINYINT NOT NULL
        CONSTRAINT DF_Deliveries_PaymentStatus DEFAULT (1);
END
GO

-- Anything already marked paid keeps that fact.
IF COL_LENGTH('dbo.Deliveries', 'HasPaid') IS NOT NULL
BEGIN
    UPDATE dbo.Deliveries SET PaymentStatus = 2 WHERE HasPaid = 1;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Deliveries_PaymentStatus')
BEGIN
    ALTER TABLE dbo.Deliveries ADD CONSTRAINT CK_Deliveries_PaymentStatus
        CHECK (PaymentStatus IN (1,2,3));
END
GO

IF COL_LENGTH('dbo.Deliveries', 'PaymentAttemptCount') IS NULL
BEGIN
    ALTER TABLE dbo.Deliveries ADD PaymentAttemptCount INT NOT NULL
        CONSTRAINT DF_Deliveries_PaymentAttempts DEFAULT (0);
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_Deliveries_PaymentAttempts')
BEGIN
    ALTER TABLE dbo.Deliveries ADD CONSTRAINT CK_Deliveries_PaymentAttempts
        CHECK (PaymentAttemptCount >= 0);
END
GO

-- Square's error code and detail, kept verbatim. PAYMENT_METHOD_ERROR means ring the
-- customer; anything else means the integration is broken. Different jobs, so the raw
-- code is worth keeping rather than flattening to a message.
IF COL_LENGTH('dbo.Deliveries', 'PaymentFailureCode') IS NULL
BEGIN
    ALTER TABLE dbo.Deliveries ADD PaymentFailureCode NVARCHAR(100) NULL;
END
GO

IF COL_LENGTH('dbo.Deliveries', 'PaymentFailureReason') IS NULL
BEGIN
    ALTER TABLE dbo.Deliveries ADD PaymentFailureReason NVARCHAR(500) NULL;
END
GO

IF COL_LENGTH('dbo.Deliveries', 'PaymentAttemptedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Deliveries ADD PaymentAttemptedAt DATETIME2(3) NULL;
END
GO

/* ============================================================================
   2. Square identifiers and what was actually charged
   ============================================================================ */

IF COL_LENGTH('dbo.Deliveries', 'SquareOrderId') IS NULL
BEGIN
    ALTER TABLE dbo.Deliveries ADD SquareOrderId NVARCHAR(128) NULL;
END
GO

IF COL_LENGTH('dbo.Deliveries', 'SquarePaymentId') IS NULL
BEGIN
    ALTER TABLE dbo.Deliveries ADD SquarePaymentId NVARCHAR(128) NULL;
END
GO

-- Square's own receipt, so staff can hand a customer a link without leaving the screen.
IF COL_LENGTH('dbo.Deliveries', 'SquareReceiptUrl') IS NULL
BEGIN
    ALTER TABLE dbo.Deliveries ADD SquareReceiptUrl NVARCHAR(500) NULL;
END
GO

/*  What Square actually took, which is the number that matters. Our own line totals are an
    estimate at snapshotted prices: Square prices from its live catalog and applies tax, and
    its figure is the one on the customer's receipt. */
IF COL_LENGTH('dbo.Deliveries', 'AmountCharged') IS NULL
BEGIN
    ALTER TABLE dbo.Deliveries ADD AmountCharged DECIMAL(10,2) NULL;
END
GO

-- The "charge all ready for this date" and "needs attention" reads.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
     WHERE name = 'IX_Deliveries_Payment' AND object_id = OBJECT_ID('dbo.Deliveries'))
BEGIN
    CREATE INDEX IX_Deliveries_Payment ON dbo.Deliveries (ScheduledFor, PaymentStatus);
END
GO

-- Square's payment id is unique per payment; a second delivery claiming one is a bug.
IF NOT EXISTS (
    SELECT 1 FROM sys.indexes
     WHERE name = 'UX_Deliveries_SquarePaymentId' AND object_id = OBJECT_ID('dbo.Deliveries'))
BEGIN
    CREATE UNIQUE INDEX UX_Deliveries_SquarePaymentId
        ON dbo.Deliveries (SquarePaymentId) WHERE SquarePaymentId IS NOT NULL;
END
GO

/* ============================================================================
   3. HasPaid goes
   ============================================================================ */

IF COL_LENGTH('dbo.Deliveries', 'HasPaid') IS NOT NULL
BEGIN
    IF EXISTS (
        SELECT 1 FROM sys.default_constraints
         WHERE parent_object_id = OBJECT_ID('dbo.Deliveries')
           AND name = 'DF_Deliveries_HasPaid')
    BEGIN
        ALTER TABLE dbo.Deliveries DROP CONSTRAINT DF_Deliveries_HasPaid;
    END

    ALTER TABLE dbo.Deliveries DROP COLUMN HasPaid;
END
GO

/* ============================================================================
   4. Selected discounts
   ============================================================================ */

IF OBJECT_ID('dbo.DeliveryDiscounts', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.DeliveryDiscounts
    (
        DeliveryId          UNIQUEIDENTIFIER    NOT NULL,

        -- Square catalog object id of the discount. The only part that is sent.
        SquareDiscountId    NVARCHAR(128)       NOT NULL,

        -- Snapshotted for display only. Nothing calculates from these; Square does the maths.
        Name                NVARCHAR(200)       NOT NULL,
        DiscountType        NVARCHAR(50)        NULL,
        Percentage          DECIMAL(5,2)        NULL,
        AmountOff           DECIMAL(10,2)       NULL,

        CreatedAt           DATETIME2(3)        NOT NULL,

        CONSTRAINT PK_DeliveryDiscounts PRIMARY KEY (DeliveryId, SquareDiscountId),
        CONSTRAINT FK_DeliveryDiscounts_Deliveries
            FOREIGN KEY (DeliveryId) REFERENCES dbo.Deliveries (Id) ON DELETE CASCADE
    );
END
GO

/* ============================================================================
   5. Permissions

   Administrator and Manager are granted every permission by a SELECT in migration 002, so
   they pick up new ones only when that script is re-run. It is already applied, so the
   grants are made explicitly here.

   Staff are granted billing:charge because they are the ones who run the day's charges.
   Narrowing that to managers later is a row in RolePermissions, not a code change.
   ============================================================================ */

MERGE dbo.Permissions AS target
USING (VALUES
    ('billing:view',   'See payment status and receipts on a delivery'),
    ('billing:charge', 'Charge a customer''s card on file through Square')
) AS source (Name, Description)
ON target.Name = source.Name
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Name, Description) VALUES (source.Name, source.Description);
GO

DECLARE @Grants TABLE (RoleId UNIQUEIDENTIFIER, PermissionName NVARCHAR(100));

INSERT INTO @Grants (RoleId, PermissionName)
VALUES
    ('11111111-1111-1111-1111-111111111111', 'billing:view'),
    ('11111111-1111-1111-1111-111111111111', 'billing:charge'),
    ('22222222-2222-2222-2222-222222222222', 'billing:view'),
    ('22222222-2222-2222-2222-222222222222', 'billing:charge'),
    ('33333333-3333-3333-3333-333333333333', 'billing:view'),
    ('33333333-3333-3333-3333-333333333333', 'billing:charge');

INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT g.RoleId, p.Id
FROM @Grants g
INNER JOIN dbo.Permissions p ON p.Name = g.PermissionName
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.RolePermissions rp
     WHERE rp.RoleId = g.RoleId AND rp.PermissionId = p.Id);
GO

/*  square:createcart is dead. The program bills Square directly — it never builds a cart for
    someone to run by hand — so no code can ever check this permission. Removed rather than
    left granted and meaningless. */
DELETE rp
  FROM dbo.RolePermissions rp
 INNER JOIN dbo.Permissions p ON p.Id = rp.PermissionId
 WHERE p.Name = 'square:createcart';
GO

DELETE FROM dbo.Permissions WHERE Name = 'square:createcart';
GO
