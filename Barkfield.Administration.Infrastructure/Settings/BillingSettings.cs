using Barkfield.Administration.Application.Services;
using Microsoft.Extensions.Options;

namespace Barkfield.Administration.Infrastructure.Settings;

/// <summary>
/// Exposes the Square billing configuration to the Application layer, which cannot see
/// Infrastructure's settings types.
/// </summary>
public class BillingSettings(IOptions<SquareSettings> square) : IBillingSettings
{
    public string? AutoshipDiscountId =>
        string.IsNullOrWhiteSpace(square.Value.AutoshipDiscountId) ? null : square.Value.AutoshipDiscountId.Trim();
}
