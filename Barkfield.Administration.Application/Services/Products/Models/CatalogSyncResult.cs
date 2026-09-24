namespace Barkfield.Administration.Application.Services.Products.Models;

/// <summary>
/// What a catalog sync did.
/// </summary>
/// <remarks>
/// A sync is a partial operation by nature: a product Square no longer has is not a failure,
/// and one bad row should not abandon the rest. The counts are reported so staff can see
/// what actually happened rather than being told only that it "worked".
/// </remarks>
/// <param name="Checked">Products compared against Square.</param>
/// <param name="Updated">Products whose name, price or SKU had changed.</param>
/// <param name="Unchanged">Products already matching Square.</param>
/// <param name="Missing">
/// Products Square no longer returns. Left untouched and listed by name — deleting them
/// would break the subscription lines that reference them.
/// </param>
public record CatalogSyncResult(
    int Checked,
    int Updated,
    int Unchanged,
    IReadOnlyCollection<string> Missing)
{
    public int MissingCount => Missing.Count;
}
