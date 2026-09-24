namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// Explains why a product ended up on a delivery, so staff can see the difference
/// between a standing order line, the current pick from a rotation, and a one-off add-on.
/// </summary>
public enum DeliveryLineSource
{
    /// <summary>A static subscription item that ships every cycle.</summary>
    Recurring = 1,

    /// <summary>The current pick from a rotation group.</summary>
    Rotation = 2,

    /// <summary>A one-time add-on that expires once the delivery completes.</summary>
    AddOn = 3,

    /// <summary>
    /// Added to this delivery by hand, after it was created.
    /// </summary>
    /// <remarks>
    /// The "she called and wants a bag added" case. A subscription add-on cannot serve it once
    /// the delivery row exists, because an add-on targets whichever delivery is next. A manual
    /// line came from a person, so it has no source row to point back at.
    /// </remarks>
    Manual = 4
}
