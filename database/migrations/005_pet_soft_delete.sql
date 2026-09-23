/*
    Barkfield Road — Auto-Ship & Logistics
    Migration 005: soft delete for pets

    Pets are deactivated rather than deleted. In a pet store the common reason a pet leaves
    the list is that it has died, and staff want it off the active view without erasing the
    record — both for the customer's history and so nobody cheerfully asks after it.

    Nothing references Pets by foreign key, so a hard delete would technically work. This is
    a deliberate choice about the business, not a constraint.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.Pets', 'IsActive') IS NULL
BEGIN
    ALTER TABLE dbo.Pets
        ADD IsActive BIT NOT NULL CONSTRAINT DF_Pets_IsActive DEFAULT (1);
END
GO

-- The customer detail page reads a customer's active pets, ordered by name.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Pets_Customer_Active' AND object_id = OBJECT_ID('dbo.Pets'))
BEGIN
    CREATE INDEX IX_Pets_Customer_Active ON dbo.Pets (CustomerId, IsActive, Name);
END
GO
