namespace Barkfield.Administration.Application.Services.Sqaure.Dtos;

/// <summary>A card Square holds on file for a customer. No card data ever reaches this system.</summary>
/// <param name="Id">Square's card id — the only token needed to charge, and all we ever store.</param>
public record SquareCardDto(
    string Id,
    string? Brand,
    string? Last4,
    int? ExpiryMonth,
    int? ExpiryYear,
    bool Enabled,
    DateTime? CreatedAt)
{
    public string Label => $"{Brand} ••••{Last4}".Trim();
}

/// <summary>One line to bill: a Square catalog variation and how many.</summary>
/// <remarks>
/// A catalog reference rather than a price, because Square's catalog is the source of truth for
/// pricing — Square looks up the price, applies tax and computes the total.
/// </remarks>
public record SquareOrderLineDto(string SquareVariationId, int Quantity);

/// <summary>What to build a Square order from.</summary>
/// <param name="ReferenceId">Our delivery id, so a Square receipt can be traced back here.</param>
/// <param name="SquareDiscountIds">Discount catalog ids. Square applies them and does the maths.</param>
public record SquareOrderRequestDto(
    string SquareCustomerId,
    string ReferenceId,
    string IdempotencyKey,
    IReadOnlyCollection<SquareOrderLineDto> Lines,
    IReadOnlyCollection<string> SquareDiscountIds);

/// <summary>A Square order, with the total Square computed for it.</summary>
/// <param name="TotalAmount">In dollars. The figure that will appear on the customer's receipt.</param>
public record SquareOrderResultDto(string OrderId, decimal TotalAmount);

/// <summary>How a charge attempt ended.</summary>
public enum SquareChargeOutcome
{
    /// <summary>Square took the money.</summary>
    Paid = 1,

    /// <summary>
    /// The card was refused. Somebody rings the customer and the card is fixed in Square.
    /// </summary>
    Declined = 2,

    /// <summary>
    /// Square rejected the request itself, or was unreachable. Nobody should be ringing a
    /// customer about this — it is ours to fix.
    /// </summary>
    Failed = 3
}

/// <summary>
/// The outcome of one charge attempt.
/// </summary>
/// <remarks>
/// <see cref="SquareChargeOutcome.Declined"/> and <see cref="SquareChargeOutcome.Failed"/> are kept
/// apart deliberately: they look identical on a screen that only says "payment failed", and they
/// need completely different people to do completely different things. The split is made on
/// Square's own error category — <c>PAYMENT_METHOD_ERROR</c> is the card, anything else is us.
/// </remarks>
public record SquareChargeResultDto(
    SquareChargeOutcome Outcome,
    string? PaymentId = null,
    decimal? AmountCharged = null,
    string? ReceiptUrl = null,
    string? ErrorCode = null,
    string? ErrorDetail = null)
{
    public bool IsPaid => Outcome == SquareChargeOutcome.Paid;

    /// <summary>A short line for the needs-attention list.</summary>
    public string Summary => Outcome switch
    {
        SquareChargeOutcome.Paid => $"Paid {AmountCharged:C}",
        SquareChargeOutcome.Declined => $"Card declined: {ErrorCode} {ErrorDetail}".Trim(),
        _ => $"Could not charge: {ErrorCode} {ErrorDetail}".Trim()
    };
}
