namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// Lifecycle state of a customer's auto-ship subscription.
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>Created but has not yet had its first delivery.</summary>
    NewSignUp = 1,

    /// <summary>Running on its normal cadence.</summary>
    Active = 2,

    /// <summary>Temporarily halted (vacation, stock issue). Resumable; contents are kept.</summary>
    Paused = 3,

    /// <summary>Ended. No further deliveries are generated.</summary>
    Canceled = 4
}
