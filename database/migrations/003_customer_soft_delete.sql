/*
    Barkfield Road — Auto-Ship & Logistics
    Migration 003: soft delete for customers

    Customers are deactivated, never removed. Deliveries hold a NO ACTION reference to
    Customers, so a customer with any delivery history cannot be hard-deleted anyway —
    this makes that the explicit, intended behaviour rather than a foreign key error.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.Customers', 'IsActive') IS NULL
BEGIN
    ALTER TABLE dbo.Customers
        ADD IsActive BIT NOT NULL CONSTRAINT DF_Customers_IsActive DEFAULT (1);
END
GO

-- The dashboard's default view: active customers, ordered by name.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Customers_Active_Name' AND object_id = OBJECT_ID('dbo.Customers'))
BEGIN
    CREATE INDEX IX_Customers_Active_Name ON dbo.Customers (IsActive, LastName, FirstName);
END
GO
