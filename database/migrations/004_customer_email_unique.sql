/*
    Barkfield Road — Auto-Ship & Logistics
    Migration 004: enforce unique customer email

    CustomerService checks for a duplicate email before inserting, but an application-level
    check is not a constraint: two concurrent creates can both pass it and both insert. This
    makes the database the authority.

    The index deliberately covers deactivated customers too. A returning customer should be
    reactivated, not duplicated, so their email stays reserved — and the create path tells
    staff when the clash is with a deactivated record.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

-- Surfaces any duplicates that already exist, so this fails loudly rather than silently
-- skipping the constraint.
IF EXISTS (SELECT 1 FROM dbo.Customers GROUP BY Email HAVING COUNT(*) > 1)
BEGIN
    THROW 50004, 'Duplicate customer emails exist. Resolve them before applying this migration.', 1;
END
GO

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Customers_Email' AND object_id = OBJECT_ID('dbo.Customers'))
BEGIN
    DROP INDEX IX_Customers_Email ON dbo.Customers;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_Customers_Email' AND object_id = OBJECT_ID('dbo.Customers'))
BEGIN
    CREATE UNIQUE INDEX UX_Customers_Email ON dbo.Customers (Email);
END
GO
