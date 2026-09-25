namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// Whether a delivery has been paid for.
/// </summary>
/// <remarks>
/// Replaces the old <c>HasPaid</c> flag, which could say paid or not-paid but not "we tried and
/// the card declined" — and that is the state the dispatch screen exists to surface, because
/// somebody has to ring the customer before the van leaves.
/// </remarks>
public enum PaymentStatus
{
    /// <summary>No charge attempted yet.</summary>
    NotCharged = 1,

    /// <summary>Square took the money. The contents are now fixed.</summary>
    Paid = 2,

    /// <summary>
    /// A charge was attempted and refused. Retryable once the customer's card is fixed in Square.
    /// </summary>
    Failed = 3
}
