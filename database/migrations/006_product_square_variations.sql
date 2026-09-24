/*
    Barkfield Road — Auto-Ship & Logistics
    Migration 006: mirror Square item variations, not items

    A "product" in Square is an ItemVariation, not an Item. The Item ("OC Raw") carries the
    name and description; the variation ("OC Raw 16 lb Venison") carries the price and SKU.
    The sellable thing — the one a subscription line points at — is the variation.

    SquareCatalogObjectId therefore holds the VARIATION id. SquareItemId is kept so a
    re-sync can find the parent, and so the picker can group variations under their item.

    Both names are stored raw because neither alone reads correctly across a real catalog:
      "OC Raw"           + "OC Raw 16 lb Venison"                    -> variation repeats the item
      "Open Farm Kibble" + "Wild-Caught Salmon & Ancient Grains 7lb" -> variation omits the brand
    Name holds the composed display string; the parts stay available for search and for a UI
    that wants to render them differently.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

IF COL_LENGTH('dbo.Products', 'SquareItemId') IS NULL
BEGIN
    ALTER TABLE dbo.Products ADD SquareItemId NVARCHAR(128) NULL;
END
GO

IF COL_LENGTH('dbo.Products', 'ItemName') IS NULL
BEGIN
    ALTER TABLE dbo.Products ADD ItemName NVARCHAR(200) NULL;
END
GO

IF COL_LENGTH('dbo.Products', 'VariationName') IS NULL
BEGIN
    ALTER TABLE dbo.Products ADD VariationName NVARCHAR(200) NULL;
END
GO

-- Null until first synced. Shown on the catalog page so staff can see how stale a price is.
IF COL_LENGTH('dbo.Products', 'LastSyncedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Products ADD LastSyncedAt DATETIME2(3) NULL;
END
GO

-- Groups variations under their parent item in the picker.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Products_SquareItemId' AND object_id = OBJECT_ID('dbo.Products'))
BEGIN
    CREATE INDEX IX_Products_SquareItemId ON dbo.Products (SquareItemId) WHERE SquareItemId IS NOT NULL;
END
GO

-- Typing "Open Farm" must find variations whose own names never mention the brand, so the
-- item name is searched alongside the composed display name.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_Products_ItemName' AND object_id = OBJECT_ID('dbo.Products'))
BEGIN
    CREATE INDEX IX_Products_ItemName ON dbo.Products (ItemName) WHERE IsActive = 1;
END
GO
