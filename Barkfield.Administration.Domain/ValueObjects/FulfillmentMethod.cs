namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// How a delivery reaches the customer. Split out of the old OrderStatus enum so that
/// "how it ships" and "where it is in the workflow" are no longer the same field.
/// </summary>
public enum FulfillmentMethod
{
    /// <summary>Driven out on a Routific route by store staff.</summary>
    LocalDelivery = 1,

    /// <summary>Customer collects in store.</summary>
    Pickup = 2,

    /// <summary>Handed to a carrier.</summary>
    Shipping = 3
}
