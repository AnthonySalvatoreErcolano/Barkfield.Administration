using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.DataAccess.Customers;

/// <summary>
/// Rebuilds the <see cref="Customer"/> aggregate from a detail read, reassembling the flattened
/// Address, TimeWindow and GeoPoint value objects.
/// </summary>
/// <remarks>
/// Shared because the deliveries slice needs a customer too — a delivery snapshots their address,
/// coordinates and preferred window when it is scheduled.
/// </remarks>
public static class CustomerRehydrator
{
    public static Customer Rehydrate(CustomerDetailDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        Address? address = null;

        if (!string.IsNullOrWhiteSpace(dto.Street) || !string.IsNullOrWhiteSpace(dto.City))
        {
            address = new Address(
                dto.Street ?? string.Empty,
                dto.City ?? string.Empty,
                dto.State ?? string.Empty,
                dto.ZipCode ?? string.Empty);
        }

        TimeWindow? preferredWindow = null;

        if (dto.PreferredWindowStart is not null && dto.PreferredWindowEnd is not null)
        {
            preferredWindow = TimeWindow.Create(dto.PreferredWindowStart.Value, dto.PreferredWindowEnd.Value);
        }

        GeoPoint? coordinates = null;

        if (dto.Latitude is not null && dto.Longitude is not null)
        {
            coordinates = GeoPoint.Create((double)dto.Latitude.Value, (double)dto.Longitude.Value);
        }

        return Customer.FromDto(
            dto.Id,
            dto.FirstName,
            dto.LastName,
            dto.Email,
            dto.PhoneNumber,
            dto.Notes,
            address,
            dto.SquareCustomerId,
            dto.IsActive,
            dto.CreatedAt,
            dto.UpdatedAt,
            coordinates,
            dto.AccessNotes,
            dto.ServiceDurationMinutes,
            preferredWindow);
    }
}
