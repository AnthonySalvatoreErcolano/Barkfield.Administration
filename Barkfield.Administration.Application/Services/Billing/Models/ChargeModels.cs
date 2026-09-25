namespace Barkfield.Administration.Application.Services.Billing.Models;

/// <summary>How a charge attempt ended, from the staff screen's point of view.</summary>
public enum ChargeOutcome
{
    /// <summary>Square took the money.</summary>
    Paid = 1,

    /// <summary>The card was refused, or there is no card. Somebody rings the customer.</summary>
    Declined = 2,

    /// <summary>Square rejected our request or was unreachable. Nobody should ring a customer.</summary>
    Failed = 3,

    /// <summary>Never sent to Square — already paid, not ready, nothing billable.</summary>
    NotAttempted = 4
}

/// <summary>
/// What happened when one delivery was charged.
/// </summary>
/// <remarks>
/// Declined and Failed stay apart because they need different people doing different things, and a
/// screen that only says "payment failed" sends staff chasing a customer over a bug in our request.
/// </remarks>
public record DeliveryChargeResult(
    Guid DeliveryId,
    string CustomerName,
    ChargeOutcome Outcome,
    decimal? AmountCharged = null,
    string? SquarePaymentId = null,
    string? ReceiptUrl = null,
    string? CardLabel = null,
    string? ErrorCode = null,
    string? Message = null)
{
    public bool IsPaid => Outcome == ChargeOutcome.Paid;

    public string OutcomeName => Outcome.ToString();

    public static DeliveryChargeResult Paid(
        Guid id, string customer, decimal amount, string paymentId, string? receiptUrl, string? cardLabel) =>
        new(id, customer, ChargeOutcome.Paid, amount, paymentId, receiptUrl, cardLabel,
            Message: $"Charged {amount:C} to {cardLabel}.");

    public static DeliveryChargeResult Declined(Guid id, string customer, string? code, string? detail) =>
        new(id, customer, ChargeOutcome.Declined, ErrorCode: code, Message: detail);

    public static DeliveryChargeResult Failed(Guid id, string customer, string? code, string? detail) =>
        new(id, customer, ChargeOutcome.Failed, ErrorCode: code, Message: detail);

    public static DeliveryChargeResult NotAttempted(Guid id, string customer, string reason) =>
        new(id, customer, ChargeOutcome.NotAttempted, Message: reason);
}

/// <summary>A delivery the batch passed over, and why.</summary>
public record ChargeSkip(Guid DeliveryId, string CustomerName, string Reason);

/// <summary>
/// What a batch charge did.
/// </summary>
/// <remarks>
/// Deliberately not a transaction: each charge moves real money and cannot be undone, so each is
/// recorded as its outcome is known. A batch-wide rollback could erase the record of money Square
/// has already taken.
/// </remarks>
public record BatchChargeResult(
    DateTime DeliveryDate,
    int Considered,
    IReadOnlyCollection<DeliveryChargeResult> Attempted,
    IReadOnlyCollection<ChargeSkip> Skipped)
{
    public int PaidCount => Attempted.Count(a => a.Outcome == ChargeOutcome.Paid);
    public int DeclinedCount => Attempted.Count(a => a.Outcome == ChargeOutcome.Declined);
    public int FailedCount => Attempted.Count(a => a.Outcome == ChargeOutcome.Failed);
    public int SkippedCount => Skipped.Count;

    public decimal TotalCharged => Attempted.Where(a => a.IsPaid).Sum(a => a.AmountCharged ?? 0m);

    /// <summary>Who to ring. The reason this returns a list rather than throwing on the first refusal.</summary>
    public IReadOnlyCollection<DeliveryChargeResult> NeedsAttention =>
        Attempted.Where(a => a.Outcome is ChargeOutcome.Declined or ChargeOutcome.Failed).ToList();
}

public enum ChargeAttentionReason
{
    /// <summary>A card was refused. Retryable once it is fixed in Square.</summary>
    PaymentFailed = 1,

    /// <summary>Paid, then a line was shorted. Refunds are made by hand in Square.</summary>
    RefundOwed = 2
}

/// <summary>One row of the needs-attention list.</summary>
public record ChargeAttentionItem(
    Guid DeliveryId,
    string CustomerName,
    DateTime ScheduledFor,
    ChargeAttentionReason Reason,
    string Detail,
    int AttemptCount)
{
    public string ReasonName => Reason.ToString();
}
