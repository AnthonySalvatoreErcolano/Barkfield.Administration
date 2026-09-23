/*
    Barkfield Road — Auto-Ship & Logistics
    Migration 002: reference data

    Idempotent — safe to re-run. Every insert is guarded, so applying this against a
    database that already has the data is a no-op.

    Role ids are fixed literals rather than NEWID() so they are identical across local,
    staging and production. That keeps a role id in a request body meaningful everywhere
    and makes environment-to-environment comparisons possible.
*/

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET XACT_ABORT ON;
GO

/* ----------------------------------------------------------------------------
   Permissions — must stay in step with Domain/Entities/Identity/Constants/Permissions.cs
   ---------------------------------------------------------------------------- */

MERGE dbo.Permissions AS target
USING (VALUES
    ('user:view',           'View staff users'),
    ('user:create',         'Create staff users'),
    ('user:edit',           'Edit staff users and their roles'),
    ('user:delete',         'Deactivate staff users'),

    ('customer:view',       'View customers, pets and delivery addresses'),
    ('customer:create',     'Create customers'),
    ('customer:edit',       'Edit customers, pets and delivery addresses'),
    ('customer:delete',     'Remove customers'),

    ('product:view',        'View the product catalog'),
    ('product:manage',      'Add and edit catalog products'),

    ('subscription:view',   'View subscriptions'),
    ('subscription:manage', 'Create and edit subscriptions, rotations and add-ons'),

    ('delivery:view',       'View deliveries and delivery history'),
    ('delivery:manage',     'Create, reschedule and cancel deliveries'),
    ('delivery:pack',       'Set per-item stock status on the packing screen'),

    ('dispatch:view',       'View the dispatch screen and route manifests'),
    ('dispatch:send',       'Send the day''s orders to the routing provider'),

    ('square:createcart',   'Create carts in Square')
) AS source (Name, Description)
ON target.Name = source.Name
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Name, Description) VALUES (source.Name, source.Description)
WHEN MATCHED AND target.Description <> source.Description THEN
    UPDATE SET Description = source.Description;
GO

/* ----------------------------------------------------------------------------
   Roles
   ---------------------------------------------------------------------------- */

MERGE dbo.Roles AS target
USING (VALUES
    ('11111111-1111-1111-1111-111111111111', 'Administrator', 'Full access to everything.',                                 1),
    ('22222222-2222-2222-2222-222222222222', 'Manager',       'Runs the operation day to day. No staff user management.',   1),
    ('33333333-3333-3333-3333-333333333333', 'Staff',         'Customer, subscription and packing work.',                   1),
    ('44444444-4444-4444-4444-444444444444', 'Driver',        'Delivery access only. No admin screens.',                    1)
) AS source (Id, Name, Description, IsSystemRole)
ON target.Id = CAST(source.Id AS UNIQUEIDENTIFIER)
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Name, Description, IsSystemRole, CreatedAt)
    VALUES (CAST(source.Id AS UNIQUEIDENTIFIER), source.Name, source.Description, source.IsSystemRole, SYSUTCDATETIME());
GO

/* ----------------------------------------------------------------------------
   Role → permission grants
   ---------------------------------------------------------------------------- */

DECLARE @Administrator UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @Manager       UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';
DECLARE @Staff         UNIQUEIDENTIFIER = '33333333-3333-3333-3333-333333333333';
DECLARE @Driver        UNIQUEIDENTIFIER = '44444444-4444-4444-4444-444444444444';

DECLARE @Grants TABLE (RoleId UNIQUEIDENTIFIER, PermissionName NVARCHAR(100));

-- Administrator: everything.
INSERT INTO @Grants (RoleId, PermissionName)
SELECT @Administrator, Name FROM dbo.Permissions;

-- Manager: the whole operation except staff user administration.
INSERT INTO @Grants (RoleId, PermissionName)
SELECT @Manager, Name FROM dbo.Permissions
WHERE Name NOT IN ('user:create', 'user:edit', 'user:delete');

-- Staff: customers, subscriptions, packing. Read-only on dispatch.
INSERT INTO @Grants (RoleId, PermissionName)
VALUES
    (@Staff, 'customer:view'),
    (@Staff, 'customer:create'),
    (@Staff, 'customer:edit'),
    (@Staff, 'product:view'),
    (@Staff, 'subscription:view'),
    (@Staff, 'subscription:manage'),
    (@Staff, 'delivery:view'),
    (@Staff, 'delivery:pack'),
    (@Staff, 'dispatch:view');

-- Driver: enough to see deliveries, nothing that edits the business.
INSERT INTO @Grants (RoleId, PermissionName)
VALUES
    (@Driver, 'delivery:view'),
    (@Driver, 'dispatch:view');

INSERT INTO dbo.RolePermissions (RoleId, PermissionId)
SELECT g.RoleId, p.Id
FROM @Grants g
INNER JOIN dbo.Permissions p ON p.Name = g.PermissionName
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.RolePermissions rp
    WHERE rp.RoleId = g.RoleId AND rp.PermissionId = p.Id
);
GO

/* ----------------------------------------------------------------------------
   Allergies — a starting list. Staff can add to this freely; nothing depends on
   these specific rows.
   ---------------------------------------------------------------------------- */

MERGE dbo.Allergies AS target
USING (VALUES
    ('Chicken'), ('Beef'), ('Lamb'), ('Pork'), ('Turkey'), ('Duck'),
    ('Fish'), ('Salmon'), ('Egg'), ('Dairy'),
    ('Wheat'), ('Corn'), ('Soy'), ('Grain'), ('Gluten'),
    ('Peanut'), ('Potato'), ('Pea'), ('Yeast'), ('Artificial preservatives')
) AS source (AllergyName)
ON target.AllergyName = source.AllergyName
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, AllergyName) VALUES (NEWID(), source.AllergyName);
GO
