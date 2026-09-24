using Barkfield.Administration.Application.DataAccess.Deliveries;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Infrastructure.Connections.Database;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Barkfield.Administration.Infrastructure.DataAccess.Deliveries;

/// <summary>
/// Writes for the delivery aggregate.
/// </summary>
/// <remarks>
/// <para>
/// <b>Lines are upserted by id.</b> Each one carries a morning's work in the stockroom — its
/// order status, how many units were physically received, the substitution snapshot and the staff
/// note explaining it. Deleting and reinserting them would throw all of that away on any save,
/// including one that only changed a note on the delivery.
/// </para>
/// <para>
/// Three statements per table — delete what is gone, update what remains, insert what is new —
/// driven by a JSON payload, all inside one transaction. Written out rather than as a MERGE for
/// the same reason as subscriptions: MERGE is shorter and easier to get subtly wrong.
/// </para>
/// <para>
/// No explicit <c>BEGIN TRANSACTION</c> here when the caller already has one: the statements run
/// on whatever transaction the executor supplies, and generation wraps a whole day in one.
/// </para>
/// </remarks>
public class DeliveryCommands : IDeliveryCommands
{
    /// <summary>
    /// DateTime is written as SQL Server parses it in an OPENJSON column: no zone suffix. The
    /// default serializer emits a trailing 'Z', which DATETIME2 conversion rejects under some
    /// server settings — a failure that would only appear once deployed.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new SqlDateTimeConverter(), new SqlNullableDateTimeConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    private readonly ISqlExecutor _sqlExecutor;

    public DeliveryCommands(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task CreateAsync(Delivery delivery, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            INSERT INTO dbo.Deliveries
                (Id, SubscriptionId, CustomerId, ScheduledFor, CompletedAt, Status, FulfillmentMethod,
                 ProcurementStatus, HasPaid, AstroCompleted, ExternalOrderId, SentToRoutingAt,
                 DeliveryStreet, DeliveryCity, DeliveryState, DeliveryZipCode,
                 DeliveryLatitude, DeliveryLongitude,
                 RequestedWindowStart, RequestedWindowEnd, ServiceDurationMinutesOverride,
                 Notes, FailureReason, CreatedAt)
            VALUES
                (@Id, @SubscriptionId, @CustomerId, @ScheduledFor, @CompletedAt, @Status, @FulfillmentMethod,
                 @ProcurementStatus, @HasPaid, @AstroCompleted, @ExternalOrderId, @SentToRoutingAt,
                 @DeliveryStreet, @DeliveryCity, @DeliveryState, @DeliveryZipCode,
                 @DeliveryLatitude, @DeliveryLongitude,
                 @RequestedWindowStart, @RequestedWindowEnd, @ServiceDurationMinutesOverride,
                 @Notes, @FailureReason, @CreatedAt);

            INSERT INTO dbo.DeliveryLines
                (Id, DeliveryId, ProductId, ProductName, UnitPrice, Quantity, Source, SourceId,
                 OrderStatus, QuantityReceived, SubstitutedWithProductId, SubstitutedWithProductName,
                 SubstitutedWithUnitPrice, StatusNote, StatusUpdatedAt)
            SELECT
                j.Id, @Id, j.ProductId, j.ProductName, j.UnitPrice, j.Quantity, j.Source, j.SourceId,
                j.OrderStatus, j.QuantityReceived, j.SubstitutedWithProductId, j.SubstitutedWithProductName,
                j.SubstitutedWithUnitPrice, j.StatusNote, j.StatusUpdatedAt
            FROM OPENJSON(@Lines) WITH (
                Id UNIQUEIDENTIFIER '$.Id',
                ProductId UNIQUEIDENTIFIER '$.ProductId',
                ProductName NVARCHAR(200) '$.ProductName',
                UnitPrice DECIMAL(10,2) '$.UnitPrice',
                Quantity INT '$.Quantity',
                Source TINYINT '$.Source',
                SourceId UNIQUEIDENTIFIER '$.SourceId',
                OrderStatus TINYINT '$.OrderStatus',
                QuantityReceived INT '$.QuantityReceived',
                SubstitutedWithProductId UNIQUEIDENTIFIER '$.SubstitutedWithProductId',
                SubstitutedWithProductName NVARCHAR(200) '$.SubstitutedWithProductName',
                SubstitutedWithUnitPrice DECIMAL(10,2) '$.SubstitutedWithUnitPrice',
                StatusNote NVARCHAR(500) '$.StatusNote',
                StatusUpdatedAt DATETIME2(3) '$.StatusUpdatedAt') j;

            COMMIT TRANSACTION;";

        await _sqlExecutor.ExecuteAsync(sql, ToParameters(delivery, expectedRevision: 0), cancellationToken);
    }

    public async Task<bool> SaveAsync(
        Delivery delivery,
        int expectedRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        /*  The head update carries the optimistic check and bumps the revision. If it matches
            nothing, the row has moved on and the batch stops before a single line is touched —
            otherwise this save's line list would delete rows another request had just added.

            The conflict path COMMITs rather than ROLLBACKs. Nothing has been modified at that
            point, and an unnamed ROLLBACK rolls back to the OUTERMOST transaction, which inside
            a caller's transaction scope would silently destroy their work — generation holds one
            around a whole day. COMMIT balances the BEGIN at either nesting level. */
        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            UPDATE dbo.Deliveries
               SET SubscriptionId                 = @SubscriptionId,
                   ScheduledFor                   = @ScheduledFor,
                   CompletedAt                    = @CompletedAt,
                   Status                         = @Status,
                   FulfillmentMethod              = @FulfillmentMethod,
                   ProcurementStatus              = @ProcurementStatus,
                   HasPaid                        = @HasPaid,
                   AstroCompleted                 = @AstroCompleted,
                   ExternalOrderId                = @ExternalOrderId,
                   SentToRoutingAt                = @SentToRoutingAt,
                   RequestedWindowStart           = @RequestedWindowStart,
                   RequestedWindowEnd             = @RequestedWindowEnd,
                   ServiceDurationMinutesOverride = @ServiceDurationMinutesOverride,
                   Notes                          = @Notes,
                   FailureReason                  = @FailureReason,
                   UpdatedAt                      = @UpdatedAt,
                   Revision                       = Revision + 1
             WHERE Id = @Id
               AND Revision = @ExpectedRevision;

            IF @@ROWCOUNT = 0
            BEGIN
                COMMIT TRANSACTION;
                SELECT CAST(0 AS INT);
                RETURN;
            END

            DELETE dl
              FROM dbo.DeliveryLines dl
             WHERE dl.DeliveryId = @Id
               AND NOT EXISTS (
                   SELECT 1 FROM OPENJSON(@Lines)
                            WITH (Id UNIQUEIDENTIFIER '$.Id') j
                    WHERE j.Id = dl.Id);

            UPDATE dl
               SET dl.Quantity                   = j.Quantity,
                   dl.OrderStatus                = j.OrderStatus,
                   dl.QuantityReceived           = j.QuantityReceived,
                   dl.SubstitutedWithProductId   = j.SubstitutedWithProductId,
                   dl.SubstitutedWithProductName = j.SubstitutedWithProductName,
                   dl.SubstitutedWithUnitPrice   = j.SubstitutedWithUnitPrice,
                   dl.StatusNote                 = j.StatusNote,
                   dl.StatusUpdatedAt            = j.StatusUpdatedAt
              FROM dbo.DeliveryLines dl
             INNER JOIN OPENJSON(@Lines) WITH (
                   Id UNIQUEIDENTIFIER '$.Id',
                   Quantity INT '$.Quantity',
                   OrderStatus TINYINT '$.OrderStatus',
                   QuantityReceived INT '$.QuantityReceived',
                   SubstitutedWithProductId UNIQUEIDENTIFIER '$.SubstitutedWithProductId',
                   SubstitutedWithProductName NVARCHAR(200) '$.SubstitutedWithProductName',
                   SubstitutedWithUnitPrice DECIMAL(10,2) '$.SubstitutedWithUnitPrice',
                   StatusNote NVARCHAR(500) '$.StatusNote',
                   StatusUpdatedAt DATETIME2(3) '$.StatusUpdatedAt') j ON j.Id = dl.Id
             WHERE dl.DeliveryId = @Id;

            INSERT INTO dbo.DeliveryLines
                (Id, DeliveryId, ProductId, ProductName, UnitPrice, Quantity, Source, SourceId,
                 OrderStatus, QuantityReceived, SubstitutedWithProductId, SubstitutedWithProductName,
                 SubstitutedWithUnitPrice, StatusNote, StatusUpdatedAt)
            SELECT
                j.Id, @Id, j.ProductId, j.ProductName, j.UnitPrice, j.Quantity, j.Source, j.SourceId,
                j.OrderStatus, j.QuantityReceived, j.SubstitutedWithProductId, j.SubstitutedWithProductName,
                j.SubstitutedWithUnitPrice, j.StatusNote, j.StatusUpdatedAt
            FROM OPENJSON(@Lines) WITH (
                Id UNIQUEIDENTIFIER '$.Id',
                ProductId UNIQUEIDENTIFIER '$.ProductId',
                ProductName NVARCHAR(200) '$.ProductName',
                UnitPrice DECIMAL(10,2) '$.UnitPrice',
                Quantity INT '$.Quantity',
                Source TINYINT '$.Source',
                SourceId UNIQUEIDENTIFIER '$.SourceId',
                OrderStatus TINYINT '$.OrderStatus',
                QuantityReceived INT '$.QuantityReceived',
                SubstitutedWithProductId UNIQUEIDENTIFIER '$.SubstitutedWithProductId',
                SubstitutedWithProductName NVARCHAR(200) '$.SubstitutedWithProductName',
                SubstitutedWithUnitPrice DECIMAL(10,2) '$.SubstitutedWithUnitPrice',
                StatusNote NVARCHAR(500) '$.StatusNote',
                StatusUpdatedAt DATETIME2(3) '$.StatusUpdatedAt') j
            WHERE NOT EXISTS (SELECT 1 FROM dbo.DeliveryLines dl WHERE dl.Id = j.Id);

            COMMIT TRANSACTION;

            SELECT CAST(1 AS INT);";

        return await _sqlExecutor.QuerySingleAsync<int>(
            sql, ToParameters(delivery, expectedRevision), cancellationToken) == 1;
    }

    /// <summary>
    /// Flattens the aggregate into head parameters plus a JSON document of its lines, unpacking
    /// the Address, GeoPoint and TimeWindow value objects.
    /// </summary>
    private static object ToParameters(Delivery delivery, int expectedRevision)
    {
        var lines = delivery.Lines
            .Select(l => new
            {
                l.Id,
                l.ProductId,
                l.ProductName,
                l.UnitPrice,
                l.Quantity,
                Source = (byte)l.Source,
                l.SourceId,
                OrderStatus = (byte)l.OrderStatus,
                l.QuantityReceived,
                l.SubstitutedWithProductId,
                l.SubstitutedWithProductName,
                l.SubstitutedWithUnitPrice,
                l.StatusNote,
                l.StatusUpdatedAt
            })
            .ToList();

        return new
        {
            delivery.Id,
            delivery.SubscriptionId,
            delivery.CustomerId,
            ScheduledFor = delivery.ScheduledFor.Date,
            delivery.CompletedAt,
            Status = (byte)delivery.Status,
            FulfillmentMethod = (byte)delivery.FulfillmentMethod,
            ProcurementStatus = (byte)delivery.ProcurementStatus,
            delivery.HasPaid,
            delivery.AstroCompleted,
            delivery.ExternalOrderId,
            delivery.SentToRoutingAt,
            DeliveryStreet = delivery.DeliveryAddress.Street,
            DeliveryCity = delivery.DeliveryAddress.City,
            DeliveryState = delivery.DeliveryAddress.State,
            DeliveryZipCode = delivery.DeliveryAddress.ZipCode,

            // GeoPoint holds doubles; the columns are DECIMAL(9,6). Cast here rather than let
            // Dapper infer a float parameter against a decimal column.
            DeliveryLatitude = (decimal?)delivery.DeliveryCoordinates?.Latitude,
            DeliveryLongitude = (decimal?)delivery.DeliveryCoordinates?.Longitude,
            RequestedWindowStart = delivery.RequestedWindow?.Start,
            RequestedWindowEnd = delivery.RequestedWindow?.End,
            delivery.ServiceDurationMinutesOverride,
            delivery.Notes,
            delivery.FailureReason,
            delivery.CreatedAt,
            UpdatedAt = delivery.UpdatedAt ?? DateTime.UtcNow,
            ExpectedRevision = expectedRevision,
            Lines = JsonSerializer.Serialize(lines, JsonOptions)
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
