namespace Barkfield.Administration.Infrastructure.Settings
{
    public class SquareSettings
    {
        public const string SectionName = "Square";

        /// <summary>
        /// Sandbox or production access token. <b>Environment variable only</b> — never in
        /// appsettings, never in the repository. Production belongs in Azure App Configuration
        /// or Key Vault.
        /// </summary>
        public string AccessToken { get; set; } = string.Empty;

        /// <summary>"Sandbox" or "Production".</summary>
        public string Environment { get; set; } = "Sandbox";

        /// <summary>
        /// The Square location every order and payment is booked against.
        /// </summary>
        /// <remarks>
        /// Configured rather than looked up, because the id differs between sandbox and production
        /// and a payment booked against the wrong location lands in the wrong set of books.
        /// </remarks>
        public string LocationId { get; set; } = string.Empty;

        /// <summary>
        /// The autoship discount applied by default to a delivery that came from a subscription.
        /// </summary>
        /// <remarks>
        /// Every autoship customer gets it, so it is defaulted rather than remembered — but staff
        /// can still change the selection per delivery. Configured because discount ids differ
        /// between sandbox and production.
        /// </remarks>
        public string AutoshipDiscountId { get; set; } = string.Empty;
    }
}
