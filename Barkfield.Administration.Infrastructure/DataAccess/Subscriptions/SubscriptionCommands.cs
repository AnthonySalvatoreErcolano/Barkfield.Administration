using Barkfield.Administration.Application.DataAccess.Subscriptions;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Infrastructure.Connections.Database;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Barkfield.Administration.Infrastructure.DataAccess.Subscriptions;

/// <summary>
/// Writes for the subscription aggregate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Children are upserted by id.</b> The obvious approach — delete the children and reinsert
/// them, as the pets slice does with allergy links — is wrong here, because every child carries
/// state that has to survive the save: <c>RotationGroups.RotationPosition</c> decides what the
/// customer receives next, <c>SubscriptionAddOns.ConsumedAt</c> is history, and
/// <c>SubscriptionItems.Id</c> is what a delivery line points back at. Recreating those rows
/// would reset rotations, re-ship consumed add-ons and orphan the delivery trail.
/// </para>
/// <para>
/// Each child table therefore gets three statements — delete what is gone, update what remains,
/// insert what is new — driven by a JSON payload of the aggregate's current children. Written
/// out rather than as a MERGE: MERGE would say it in one statement, but it is easy to get
/// subtly wrong around <c>NOT MATCHED BY SOURCE</c> scoping, and this code is the only thing
/// standing between an edit and a customer's standing order.
/// </para>
/// <para>
/// Rotation groups are handled before their items, so a removed group takes its items with it
/// through the existing cascade instead of being fought over by two statements.
/// </para>
/// </remarks>
public class SubscriptionCommands : ISubscriptionCommands
{
    private readonly ISqlExecutor _sqlExecutor;

    /// <summary>
    /// DateTime is written as SQL Server parses it in an OPENJSON column: no zone suffix.
    /// The default serializer emits a trailing 'Z', which DATETIME2 conversion rejects under
    /// some server settings — a failure that would only show up once deployed.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new SqlDateTimeConverter(), new SqlNullableDateTimeConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public SubscriptionCommands(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task CreateAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        // A new subscription has no children yet — the domain creates it empty and it cannot be
        // activated until something is added.
        const string sql = @"
            INSERT INTO dbo.Subscriptions
                (Id, CustomerId, Name, Status, FrequencyInterval, FrequencyUnit, FulfillmentMethod,
                 NextDeliveryDate, LastDeliveryDate, SignUpDate, PausedUntil, CreatedAt)
            VALUES
                (@Id, @CustomerId, @Name, @Status, @FrequencyInterval, @FrequencyUnit, @FulfillmentMethod,
                 @NextDeliveryDate, @LastDeliveryDate, @SignUpDate, @PausedUntil, @CreatedAt);";

        await _sqlExecutor.ExecuteAsync(sql, ToHeadParameters(subscription), cancellationToken);
    }

    public async Task<bool> SaveAsync(
        Subscription subscription,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        // The head update carries the optimistic check and bumps the revision. If it matches
        // nothing the row has moved on (or gone), so the whole batch is abandoned before a
        // single child is touched — otherwise this save's child lists would delete rows another
        // request had just added.
        //
        // The check is on Revision rather than UpdatedAt: SqlClient sends a .NET DateTime as
        // SqlDbType.DATETIME (~3.33ms granularity), so a value read out of a DATETIME2(3)
        // column does not compare equal to itself when sent back — and two saves in the same
        // millisecond would share a timestamp and let a real conflict through.
        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            UPDATE dbo.Subscriptions
               SET Name              = @Name,
                   Status            = @Status,
                   FrequencyInterval = @FrequencyInterval,
                   FrequencyUnit     = @FrequencyUnit,
                   FulfillmentMethod = @FulfillmentMethod,
                   NextDeliveryDate  = @NextDeliveryDate,
                   LastDeliveryDate  = @LastDeliveryDate,
                   PausedUntil       = @PausedUntil,
                   UpdatedAt         = @UpdatedAt,
                   Revision          = Revision + 1
             WHERE Id = @Id
               AND Revision = @ExpectedRevision;

            /*  Conflict: the head update matched nothing, so nothing has been modified and there
                is nothing to undo. COMMIT rather than ROLLBACK, because an unnamed ROLLBACK
                rolls back to the OUTERMOST transaction — inside a caller's transaction scope
                that would silently destroy their work and leave their COMMIT to fail with a
                mismatched transaction count. Committing a transaction that changed nothing is a
                no-op, and it balances the BEGIN above at either nesting level. */
            IF @@ROWCOUNT = 0
            BEGIN
                COMMIT TRANSACTION;
                SELECT CAST(0 AS INT);
                RETURN;
            END

            /* ---- Rotation groups, before their items: a removed group cascades its own ---- */

            DELETE rg
              FROM dbo.RotationGroups rg
             WHERE rg.SubscriptionId = @Id
               AND NOT EXISTS (
                   SELECT 1 FROM OPENJSON(@RotationGroups)
                            WITH (Id UNIQUEIDENTIFIER '$.Id') j
                    WHERE j.Id = rg.Id);

            UPDATE rg
               SET rg.Name             = j.Name,
                   rg.RotationPosition = j.RotationPosition,
                   rg.IsActive         = j.IsActive,
                   rg.UpdatedAt        = j.UpdatedAt
              FROM dbo.RotationGroups rg
             INNER JOIN OPENJSON(@RotationGroups)
                   WITH (Id UNIQUEIDENTIFIER '$.Id',
                         Name NVARCHAR(100) '$.Name',
                         RotationPosition INT '$.RotationPosition',
                         IsActive BIT '$.IsActive',
                         UpdatedAt DATETIME2(3) '$.UpdatedAt') j ON j.Id = rg.Id
             WHERE rg.SubscriptionId = @Id;

            INSERT INTO dbo.RotationGroups
                (Id, SubscriptionId, Name, RotationPosition, IsActive, CreatedAt, UpdatedAt)
            SELECT j.Id, @Id, j.Name, j.RotationPosition, j.IsActive, j.CreatedAt, j.UpdatedAt
              FROM OPENJSON(@RotationGroups)
                   WITH (Id UNIQUEIDENTIFIER '$.Id',
                         Name NVARCHAR(100) '$.Name',
                         RotationPosition INT '$.RotationPosition',
                         IsActive BIT '$.IsActive',
                         CreatedAt DATETIME2(3) '$.CreatedAt',
                         UpdatedAt DATETIME2(3) '$.UpdatedAt') j
             WHERE NOT EXISTS (SELECT 1 FROM dbo.RotationGroups rg WHERE rg.Id = j.Id);

            /* ---- Rotation group items ---- */

            DELETE rgi
              FROM dbo.RotationGroupItems rgi
             INNER JOIN dbo.RotationGroups rg ON rg.Id = rgi.RotationGroupId
             WHERE rg.SubscriptionId = @Id
               AND NOT EXISTS (
                   SELECT 1 FROM OPENJSON(@RotationItems)
                            WITH (Id UNIQUEIDENTIFIER '$.Id') j
                    WHERE j.Id = rgi.Id);

            UPDATE rgi
               SET rgi.SequenceOrder = j.SequenceOrder,
                   rgi.Quantity      = j.Quantity,
                   rgi.UpdatedAt     = j.UpdatedAt
              FROM dbo.RotationGroupItems rgi
             INNER JOIN OPENJSON(@RotationItems)
                   WITH (Id UNIQUEIDENTIFIER '$.Id',
                         SequenceOrder INT '$.SequenceOrder',
                         Quantity INT '$.Quantity',
                         UpdatedAt DATETIME2(3) '$.UpdatedAt') j ON j.Id = rgi.Id;

            INSERT INTO dbo.RotationGroupItems
                (Id, RotationGroupId, ProductId, SequenceOrder, Quantity, CreatedAt, UpdatedAt)
            SELECT j.Id, j.RotationGroupId, j.ProductId, j.SequenceOrder, j.Quantity, j.CreatedAt, j.UpdatedAt
              FROM OPENJSON(@RotationItems)
                   WITH (Id UNIQUEIDENTIFIER '$.Id',
                         RotationGroupId UNIQUEIDENTIFIER '$.RotationGroupId',
                         ProductId UNIQUEIDENTIFIER '$.ProductId',
                         SequenceOrder INT '$.SequenceOrder',
                         Quantity INT '$.Quantity',
                         CreatedAt DATETIME2(3) '$.CreatedAt',
                         UpdatedAt DATETIME2(3) '$.UpdatedAt') j
             WHERE NOT EXISTS (SELECT 1 FROM dbo.RotationGroupItems rgi WHERE rgi.Id = j.Id);

            /* ---- Static items ---- */

            DELETE si
              FROM dbo.SubscriptionItems si
             WHERE si.SubscriptionId = @Id
               AND NOT EXISTS (
                   SELECT 1 FROM OPENJSON(@Items)
                            WITH (Id UNIQUEIDENTIFIER '$.Id') j
                    WHERE j.Id = si.Id);

            UPDATE si
               SET si.Quantity  = j.Quantity,
                   si.UpdatedAt = j.UpdatedAt
              FROM dbo.SubscriptionItems si
             INNER JOIN OPENJSON(@Items)
                   WITH (Id UNIQUEIDENTIFIER '$.Id',
                         Quantity INT '$.Quantity',
                         UpdatedAt DATETIME2(3) '$.UpdatedAt') j ON j.Id = si.Id
             WHERE si.SubscriptionId = @Id;

            INSERT INTO dbo.SubscriptionItems
                (Id, SubscriptionId, ProductId, Quantity, CreatedAt, UpdatedAt)
            SELECT j.Id, @Id, j.ProductId, j.Quantity, j.CreatedAt, j.UpdatedAt
              FROM OPENJSON(@Items)
                   WITH (Id UNIQUEIDENTIFIER '$.Id',
                         ProductId UNIQUEIDENTIFIER '$.ProductId',
                         Quantity INT '$.Quantity',
                         CreatedAt DATETIME2(3) '$.CreatedAt',
                         UpdatedAt DATETIME2(3) '$.UpdatedAt') j
             WHERE NOT EXISTS (SELECT 1 FROM dbo.SubscriptionItems si WHERE si.Id = j.Id);

            /* ---- Add-ons. Only pending ones are loaded, so only pending ones can be
                    deleted here — a consumed add-on is history and is never in the payload. ---- */

            DELETE sa
              FROM dbo.SubscriptionAddOns sa
             WHERE sa.SubscriptionId = @Id
               AND sa.ConsumedAt IS NULL
               AND NOT EXISTS (
                   SELECT 1 FROM OPENJSON(@AddOns)
                            WITH (Id UNIQUEIDENTIFIER '$.Id') j
                    WHERE j.Id = sa.Id);

            UPDATE sa
               SET sa.Quantity   = j.Quantity,
                   sa.Note       = j.Note,
                   sa.ConsumedAt = j.ConsumedAt
              FROM dbo.SubscriptionAddOns sa
             INNER JOIN OPENJSON(@AddOns)
                   WITH (Id UNIQUEIDENTIFIER '$.Id',
                         Quantity INT '$.Quantity',
                         Note NVARCHAR(500) '$.Note',
                         ConsumedAt DATETIME2(3) '$.ConsumedAt') j ON j.Id = sa.Id
             WHERE sa.SubscriptionId = @Id;

            INSERT INTO dbo.SubscriptionAddOns
                (Id, SubscriptionId, ProductId, Quantity, Note, CreatedAt, ConsumedAt)
            SELECT j.Id, @Id, j.ProductId, j.Quantity, j.Note, j.CreatedAt, j.ConsumedAt
              FROM OPENJSON(@AddOns)
                   WITH (Id UNIQUEIDENTIFIER '$.Id',
                         ProductId UNIQUEIDENTIFIER '$.ProductId',
                         Quantity INT '$.Quantity',
                         Note NVARCHAR(500) '$.Note',
                         CreatedAt DATETIME2(3) '$.CreatedAt',
                         ConsumedAt DATETIME2(3) '$.ConsumedAt') j
             WHERE NOT EXISTS (SELECT 1 FROM dbo.SubscriptionAddOns sa WHERE sa.Id = j.Id);

            COMMIT TRANSACTION;

            SELECT CAST(1 AS INT);";

        return await _sqlExecutor.QuerySingleAsync<int>(
            sql, ToSaveParameters(subscription, expectedRevision), cancellationToken) == 1;
    }

    private static object ToHeadParameters(Subscription subscription) => new
    {
        subscription.Id,
        subscription.CustomerId,
        subscription.Name,
        Status = (byte)subscription.Status,
        FrequencyInterval = subscription.Frequency.Interval,
        FrequencyUnit = (byte)subscription.Frequency.Unit,
        FulfillmentMethod = (byte)subscription.FulfillmentMethod,
        NextDeliveryDate = subscription.NextDeliveryDate.Date,
        LastDeliveryDate = subscription.LastDeliveryDate?.Date,
        subscription.SignUpDate,
        PausedUntil = subscription.PausedUntil?.Date,
        subscription.CreatedAt,
        UpdatedAt = subscription.UpdatedAt ?? DateTime.UtcNow
    };

    /// <summary>
    /// Flattens the aggregate into the head parameters plus one JSON document per child table.
    /// </summary>
    private static object ToSaveParameters(Subscription subscription, int expectedRevision)
    {
        var items = subscription.Items
            .Select(i => new
            {
                i.Id,
                i.ProductId,
                i.Quantity,
                i.CreatedAt,
                i.UpdatedAt
            })
            .ToList();

        var groups = subscription.RotationGroups
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.RotationPosition,
                g.IsActive,
                g.CreatedAt,
                g.UpdatedAt
            })
            .ToList();

        // Flattened across groups: one payload drives one set of statements, and each row
        // already knows which group it belongs to.
        var rotationItems = subscription.RotationGroups
            .SelectMany(g => g.Items.Select(i => new
            {
                i.Id,
                RotationGroupId = g.Id,
                i.ProductId,
                i.SequenceOrder,
                i.Quantity,
                i.CreatedAt,
                i.UpdatedAt
            }))
            .ToList();

        // Every add-on the aggregate holds, pending or just-consumed. CompleteDelivery marks
        // add-ons consumed in memory, so the update statement is what records that.
        var addOns = subscription.AddOns
            .Select(a => new
            {
                a.Id,
                a.ProductId,
                a.Quantity,
                a.Note,
                a.CreatedAt,
                a.ConsumedAt
            })
            .ToList();

        return new
        {
            subscription.Id,
            subscription.CustomerId,
            subscription.Name,
            Status = (byte)subscription.Status,
            FrequencyInterval = subscription.Frequency.Interval,
            FrequencyUnit = (byte)subscription.Frequency.Unit,
            FulfillmentMethod = (byte)subscription.FulfillmentMethod,
            NextDeliveryDate = subscription.NextDeliveryDate.Date,
            LastDeliveryDate = subscription.LastDeliveryDate?.Date,
            subscription.SignUpDate,
            PausedUntil = subscription.PausedUntil?.Date,
            subscription.CreatedAt,
            UpdatedAt = subscription.UpdatedAt ?? DateTime.UtcNow,
            ExpectedRevision = expectedRevision,
            Items = JsonSerializer.Serialize(items, JsonOptions),
            RotationGroups = JsonSerializer.Serialize(groups, JsonOptions),
            RotationItems = JsonSerializer.Serialize(rotationItems, JsonOptions),
            AddOns = JsonSerializer.Serialize(addOns, JsonOptions)
        };
    }

    /// <summary>Writes a DateTime in the form OPENJSON's DATETIME2 column accepts.</summary>
    private sealed class SqlDateTimeConverter : JsonConverter<DateTime>
    {
        private const string Format = "yyyy-MM-ddTHH:mm:ss.fff";

        public override DateTime Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.GetDateTime();

        public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString(Format));
    }

    private sealed class SqlNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        private const string Format = "yyyy-MM-ddTHH:mm:ss.fff";

        public override DateTime? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTime();

        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            if (value is null) writer.WriteNullValue();
            else writer.WriteStringValue(value.Value.ToString(Format));
        }
    }
}
