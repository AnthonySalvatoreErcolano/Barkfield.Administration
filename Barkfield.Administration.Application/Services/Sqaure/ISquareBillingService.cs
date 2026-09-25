using Barkfield.Administration.Application.Services.Sqaure.Dtos;

namespace Barkfield.Administration.Application.Services.Sqaure;

/// <summary>
/// Charging a customer's card on file through Square.
/// </summary>
/// <remarks>
/// <para>
/// Two calls, not one, and in this order: create the order, persist its id, then charge. A failed
/// payment leaves the Square order open, so reusing it across retries avoids abandoning a new
/// order behind every decline.
/// </para>
/// <para>
/// No card data ever enters this application — only Square's card id. That is what keeps the
/// project out of PCI scope, and it is why there is no method here for adding or editing a card:
/// staff do that in Square.
/// </para>
/// </remarks>
public interface ISquareBillingService
{
    /// <summary>
    /// The card to charge: the most recently added one that is still enabled.
    /// </summary>
    /// <remarks>
    /// Null when the customer has no usable card, which has to be caught before any charge is
    /// attempted. Customers do sometimes have several; the newest is the one they most recently
    /// handed over.
    /// </remarks>
    Task<SquareCardDto?> GetCardToChargeAsync(string squareCustomerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds a Square order from catalog references and discount ids, and returns the total
    /// Square computed for it.
    /// </summary>
    Task<SquareOrderResultDto> CreateOrderAsync(
        SquareOrderRequestDto request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Charges a card against an existing order.
    /// </summary>
    /// <param name="idempotencyKey">
    /// Must be unique per <i>attempt</i>. Square returns the original payment for a repeat of the
    /// same key with the same payload, and refuses a repeat with a different one — so a retry
    /// after a decline needs a fresh key or it cannot go through at all.
    /// </param>
    /// <remarks>
    /// Never throws for a refused card: a decline is an outcome, not a fault. Only a genuine
    /// integration problem is reported as <see cref="SquareChargeOutcome.Failed"/>.
    /// </remarks>
    Task<SquareChargeResultDto> ChargeAsync(
        string squareCustomerId,
        string squareCardId,
        string squareOrderId,
        decimal amount,
        string idempotencyKey,
        string referenceId,
        CancellationToken cancellationToken = default);
}
