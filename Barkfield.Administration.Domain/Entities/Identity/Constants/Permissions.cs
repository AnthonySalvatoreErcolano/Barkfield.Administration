namespace Barkfield.Administration.Domain.Entities.Identity.Constants;

/// <summary>
/// Permission keys used by <c>[RequirePermission]</c>. Every value here must exist as a row in
/// the Permissions table — the seed script is the source of truth for that.
/// </summary>
/// <remarks>
/// Format is <c>area:action</c>, lower case. Add a constant and a seed row together.
/// </remarks>
public static class Permissions
{
    public static class Users
    {
        public const string View = "user:view";
        public const string Create = "user:create";
        public const string Edit = "user:edit";
        public const string Delete = "user:delete";
    }

    public static class Customers
    {
        public const string View = "customer:view";
        public const string Create = "customer:create";
        public const string Edit = "customer:edit";
        public const string Delete = "customer:delete";
    }

    public static class Products
    {
        public const string View = "product:view";
        public const string Manage = "product:manage";
    }

    public static class Subscriptions
    {
        public const string View = "subscription:view";
        public const string Manage = "subscription:manage";
    }

    public static class Deliveries
    {
        public const string View = "delivery:view";
        public const string Manage = "delivery:manage";

        /// <summary>Toggling per-item stock status on the packing screen.</summary>
        public const string Pack = "delivery:pack";
    }

    public static class Dispatch
    {
        public const string View = "dispatch:view";

        /// <summary>Sending the day's orders to the routing provider.</summary>
        public const string Send = "dispatch:send";
    }

    /// <summary>
    /// Billing a delivery through Square.
    /// </summary>
    /// <remarks>
    /// Replaces the old <c>square:createcart</c>, which described a workflow this application no
    /// longer has — it bills Square directly rather than building a cart for someone to run by
    /// hand. Migration 009 removes the dead permission.
    /// </remarks>
    public static class Billing
    {
        public const string View = "billing:view";

        /// <summary>Charging a customer's card on file. Granted to staff, who run the day's charges.</summary>
        public const string Charge = "billing:charge";
    }
}
