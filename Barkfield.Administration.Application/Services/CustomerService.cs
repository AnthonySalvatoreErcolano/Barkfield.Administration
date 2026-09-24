using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Application.Services.Sqaure.Dtos;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Barkfield.Administration.Application.Services;

/// <summary>
/// The outcome of a write that also tries to reach Square.
/// </summary>
/// <param name="SquareSynced">
/// False when the local write succeeded but Square did not. The record is correct here;
/// the Square profile is behind and can be retried.
/// </param>
public sealed record CustomerWriteResult(Guid CustomerId, bool SquareSynced, string? SquareError = null);

/// <summary>
/// Customer workflows, including keeping the Square profile in step.
/// </summary>
/// <remarks>
/// <para>
/// <b>Local first, Square second, and a Square failure never fails the request.</b> This
/// application is where staff work; they should not be blocked from recording a customer
/// because Square is unreachable. A customer whose Square push failed simply has no
/// <c>SquareCustomerId</c>, which the dashboard can filter on and
/// <see cref="SyncToSquareAsync"/> can repair.
/// </para>
/// <para>
/// The reverse order — Square first — leaves an orphaned Square profile whenever the
/// local insert fails, which is harder to notice and harder to clean up.
/// </para>
/// </remarks>
public class CustomerService
{
    private readonly ISquareService _squareService;
    private readonly ICustomerCommands _customerCommands;
    private readonly ICustomerQueries _customerQueries;
    private readonly ILogger<CustomerService> _logger;

    public CustomerService(
        ISquareService squareService,
        ICustomerCommands customerCommands,
        ICustomerQueries customerQueries,
        ILogger<CustomerService> logger)
    {
        _squareService = squareService;
        _customerCommands = customerCommands;
        _customerQueries = customerQueries;
        _logger = logger;
    }

    /// <summary>
    /// Searches Square for existing profiles matching the contact details, so staff can
    /// link an existing customer instead of creating a duplicate.
    /// </summary>
    public Task<IEnumerable<SquareCustomerCandidateDto>> SearchSquareCustomersAsync(
        string email,
        string? phoneNumber,
        CancellationToken cancellationToken = default) =>
        _squareService.SearchCustomersAsync(email, phoneNumber, cancellationToken);

    /// <summary>
    /// Creates a customer locally, then provisions or links their Square profile.
    /// </summary>
    /// <param name="squareCustomerId">
    /// Supplied when staff picked an existing Square profile from the duplicate search.
    /// When null, a new Square profile is created.
    /// </param>
    public async Task<CustomerWriteResult> CreateCustomerAsync(
        string firstName,
        string lastName,
        string email,
        string? phoneNumber,
        string? notes,
        Address? address,
        string? squareCustomerId,
        CancellationToken cancellationToken = default)
    {
        if (await _customerQueries.EmailExistsAsync(email, null, cancellationToken))
        {
            // Point staff at the existing record rather than leaving them stuck. A returning
            // customer should be reactivated, not duplicated.
            CustomerDetailDto? clash = await _customerQueries.GetByEmailAsync(email, cancellationToken);

            throw new ValidationException(clash is { IsActive: false }
                ? $"A deactivated customer ('{clash.FullName}') already uses the email '{email}'. Reactivate them instead of creating a duplicate."
                : $"A customer with the email '{email}' already exists.");
        }

        // Runs the domain's validation and normalisation before anything is persisted.
        Customer customer = Customer.Create(
            firstName, lastName, email, phoneNumber, address, notes, squareCustomerId);

        await _customerCommands.CreateAsync(customer, cancellationToken);

        // Staff picked an existing Square profile — nothing left to provision.
        if (!string.IsNullOrWhiteSpace(squareCustomerId))
        {
            return new CustomerWriteResult(customer.Id, SquareSynced: true);
        }

        try
        {
            string newSquareId = await _squareService.CreateCustomerAsync(
                customer.FirstName,
                customer.LastName,
                customer.Email,
                customer.PhoneNumber,
                customer.Address,
                customer.Notes,
                cancellationToken);

            await _customerCommands.LinkSquareCustomerAsync(customer.Id, newSquareId, cancellationToken);

            return new CustomerWriteResult(customer.Id, SquareSynced: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Customer {CustomerId} was created locally but could not be provisioned in Square.",
                customer.Id);

            return new CustomerWriteResult(customer.Id, SquareSynced: false, SquareError: ex.Message);
        }
    }

    /// <summary>
    /// Applies an edit locally, then pushes it to Square if the customer is linked.
    /// </summary>
    public async Task<CustomerWriteResult> UpdateCustomerAsync(
        Guid customerId,
        string firstName,
        string lastName,
        string email,
        string? phoneNumber,
        string? notes,
        Address? address,
        CancellationToken cancellationToken = default)
    {
        CustomerDetailDto existing = await _customerQueries.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer with ID '{customerId}' was not found.");

        if (await _customerQueries.EmailExistsAsync(email, customerId, cancellationToken))
        {
            throw new ValidationException($"Another customer already uses the email '{email}'.");
        }

        Customer customer = Rehydrate(existing);
        customer.Update(firstName, lastName, email, phoneNumber, notes, address);

        await _customerCommands.UpdateAsync(customer, cancellationToken);

        if (string.IsNullOrWhiteSpace(customer.SquareCustomerId))
        {
            // Never linked. Not an error — the dashboard flags it and it can be synced later.
            return new CustomerWriteResult(customer.Id, SquareSynced: false);
        }

        try
        {
            await PushToSquareAsync(customer, cancellationToken);
            return new CustomerWriteResult(customer.Id, SquareSynced: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Customer {CustomerId} was updated locally but the change could not be pushed to Square.",
                customer.Id);

            return new CustomerWriteResult(customer.Id, SquareSynced: false, SquareError: ex.Message);
        }
    }

    /// <summary>
    /// Retries the Square link for a customer whose earlier sync failed, or pushes the
    /// current details for one that is already linked.
    /// </summary>
    public async Task<CustomerWriteResult> SyncToSquareAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        CustomerDetailDto existing = await _customerQueries.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer with ID '{customerId}' was not found.");

        Customer customer = Rehydrate(existing);

        if (!string.IsNullOrWhiteSpace(customer.SquareCustomerId))
        {
            await PushToSquareAsync(customer, cancellationToken);
            return new CustomerWriteResult(customer.Id, SquareSynced: true);
        }

        string newSquareId = await _squareService.CreateCustomerAsync(
            customer.FirstName,
            customer.LastName,
            customer.Email,
            customer.PhoneNumber,
            customer.Address,
            customer.Notes,
            cancellationToken);

        await _customerCommands.LinkSquareCustomerAsync(customer.Id, newSquareId, cancellationToken);

        return new CustomerWriteResult(customer.Id, SquareSynced: true);
    }

    /// <summary>
    /// Deactivates a customer. Their delivery history is retained, which is why there is
    /// no hard delete.
    /// </summary>
    public async Task DeactivateCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        if (!await _customerCommands.SetActiveAsync(customerId, false, cancellationToken))
        {
            throw new NotFoundException($"Customer with ID '{customerId}' was not found.");
        }
    }

    public async Task ReactivateCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        if (!await _customerCommands.SetActiveAsync(customerId, true, cancellationToken))
        {
            throw new NotFoundException($"Customer with ID '{customerId}' was not found.");
        }
    }

    private Task PushToSquareAsync(Customer customer, CancellationToken cancellationToken) =>
        _squareService.UpdateCustomerAsync(
            new UpdateSquareCustomerDto
            {
                SquareCustomerId = customer.SquareCustomerId!,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                Address = customer.Address!,
                Notes = customer.Notes!
            },
            cancellationToken);

    private static Customer Rehydrate(CustomerDetailDto dto)
    {
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

    /// <summary>
    /// Updates the driver-facing detail for a customer's stop.
    /// </summary>
    /// <remarks>
    /// Separate from the customer edit because this is operational information dispatch
    /// maintains — and because routing it through the edit form would let a form that does not
    /// collect a gate code silently clear one.
    /// </remarks>
    public async Task UpdateDeliveryDetailsAsync(
        Guid customerId,
        string? accessNotes,
        int? serviceDurationMinutes,
        TimeOnly? preferredWindowStart,
        TimeOnly? preferredWindowEnd,
        CancellationToken cancellationToken = default)
    {
        CustomerDetailDto dto = await _customerQueries.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer with ID '{customerId}' was not found.");

        Customer customer = Rehydrate(dto);

        TimeWindow? window = null;

        if (preferredWindowStart is not null || preferredWindowEnd is not null)
        {
            if (preferredWindowStart is null || preferredWindowEnd is null)
            {
                throw new ValidationException("A delivery window needs both a start and an end time.");
            }

            // TimeWindow.Create rejects an end at or before the start, which surfaces as a 400.
            window = TimeWindow.Create(preferredWindowStart.Value, preferredWindowEnd.Value);
        }

        customer.UpdateDeliveryDetails(accessNotes, serviceDurationMinutes, window);

        await _customerCommands.UpdateAsync(customer, cancellationToken);
    }
}
