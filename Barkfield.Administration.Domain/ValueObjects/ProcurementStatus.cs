namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// Delivery-wide rollup of every line's <see cref="LineOrderStatus"/> — the at-a-glance
/// "can we pack this yet?" answer.
/// </summary>
/// <remarks>
/// Derived from the lines, never edited directly. The owning Delivery recalculates it on every
/// line change and persists the result, so it can be indexed and filtered in SQL
/// ("show me everything blocked") without drifting from the lines it summarises.
/// </remarks>
public enum ProcurementStatus
{
    /// <summary>No line has been actioned yet.</summary>
    NotStarted = 1,

    /// <summary>Some lines actioned, none blocking.</summary>
    InProgress = 2,

    /// <summary>At least one line is out of stock and needs a decision. Cannot be packed.</summary>
    Blocked = 3,

    /// <summary>Every line is received, substituted or knowingly shorted. Ready to pack.</summary>
    Ready = 4
}
