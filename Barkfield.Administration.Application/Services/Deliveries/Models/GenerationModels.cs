using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.Services.Deliveries.Models;

/// <summary>
/// A subscription that generation would pick up for a given date, and whether it actually will.
/// </summary>
/// <remarks>
/// Backs the preview, so staff can look before committing. Every reason generation would pass
/// something over is stated here rather than discovered afterwards.
/// </remarks>
public class DueSubscriptionDto
{
    public Guid SubscriptionId { get; set; }
    public string? SubscriptionName { get; set; }

    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    public DateTime NextDeliveryDate { get; set; }
    public SubscriptionStatus Status { get; set; }
    public FulfillmentMethod FulfillmentMethod { get; set; }

    /// <summary>Set when the subscription is paused with a return date that has come round.</summary>
    public DateTime? PausedUntil { get; set; }

    /// <summary>False when a local-delivery customer has no address, which blocks generation.</summary>
    public bool CustomerHasAddress { get; set; }

    /// <summary>True when a non-cancelled delivery already exists for this date.</summary>
    public bool AlreadyGenerated { get; set; }

    /// <summary>Recurring lines and active rotations. Zero means nothing would ship.</summary>
    public int ScheduledLineCount { get; set; }

    public string StatusName => Status.ToString();
    public string FulfillmentMethodName => FulfillmentMethod.ToString();

    /// <summary>True when this subscription is being brought back from a dated pause.</summary>
    public bool ResumingFromPause => Status == SubscriptionStatus.Paused && PausedUntil is not null;

    /// <summary>
    /// The subscription's next delivery is behind the date being generated — it was missed on the
    /// day and is being caught up. Worth surfacing so a stale date is visible rather than silent.
    /// </summary>
    public bool IsOverdue { get; set; }

    /// <summary>Why generation would skip this, or null when it will proceed.</summary>
    public string? SkipReason
    {
        get
        {
            if (AlreadyGenerated) return "A delivery already exists for this date.";
            if (ScheduledLineCount == 0) return "Nothing is scheduled to ship.";

            if (FulfillmentMethod == FulfillmentMethod.LocalDelivery && !CustomerHasAddress)
                return "The customer has no address on file for a local delivery.";

            return null;
        }
    }

    public bool WillGenerate => SkipReason is null;
}

/// <summary>A subscription generation passed over, and why.</summary>
public record GenerationSkip(
    Guid SubscriptionId,
    string SubscriptionName,
    string CustomerName,
    string Reason);

/// <summary>
/// What a generation run did.
/// </summary>
/// <remarks>
/// Partial success is the normal outcome, not an error: one customer missing an address should not
/// stop the rest of the day being created. The counts and reasons are reported so staff can see
/// what happened instead of being told only that it "worked".
/// </remarks>
/// <param name="ResumedFromPause">
/// Subscriptions brought back from a dated pause by this run. Nothing else in the system acts on
/// that date, so this is where it takes effect — and it is surfaced because a customer's order
/// restarting is worth seeing.
/// </param>
public record DeliveryGenerationResult(
    DateTime DeliveryDate,
    int Considered,
    IReadOnlyCollection<Guid> CreatedDeliveryIds,
    IReadOnlyCollection<string> ResumedFromPause,
    IReadOnlyCollection<GenerationSkip> Skipped)
{
    public int Created => CreatedDeliveryIds.Count;
    public int SkippedCount => Skipped.Count;
    public int ResumedCount => ResumedFromPause.Count;
}
