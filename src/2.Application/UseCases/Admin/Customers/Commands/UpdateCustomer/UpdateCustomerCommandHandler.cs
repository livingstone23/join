using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Customers.Commands;

/// <summary>
/// Handles the update of an existing Customer aggregate, including collection synchronization.
/// </summary>
/// <param name="unitOfWork">Unit of Work used for transactional persistence.</param>
/// <param name="customerMapper">Mapperly mapper for customer transformations.</param>
/// <param name="currentUserService">Current tenant context.</param>
public class UpdateCustomerCommandHandler(
    IUnitOfWork unitOfWork,
    ICustomerMapper customerMapper,
    ICurrentUserService currentUserService) : IRequestHandler<UpdateCustomerCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly ICustomerMapper _customerMapper = customerMapper;

    /// <summary>
    /// Updates a customer aggregate and synchronizes addresses and contacts atomically.
    /// </summary>
    /// <param name="request">The command payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A response containing the updated customer identifier.</returns>
    public async Task<Response<Guid>> Handle(UpdateCustomerCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The X-Company-Id header is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var company = await companyRepository.GetAsync(currentUserService.CompanyId);
        if (company is null)
        {
            return Response<Guid>.Error(
                "INVALID_COMPANY",
                ["The provided company does not exist or is inactive."]);
        }

        var customerEntity = await _unitOfWork.Customers.GetForUpdateAsync(request.Id, currentUserService.CompanyId);
        if (customerEntity is null)
        {
            return Response<Guid>.Error("CUSTOMER_NOT_FOUND", ["Customer not found for the current company."]);
        }

        var identificationTypeRepository = _unitOfWork.GetRepository<IdentificationType>();
        var identificationType = await identificationTypeRepository.GetAsync(request.IdentificationTypeId);
        if (identificationType is null)
        {
            return Response<Guid>.Error(
                "INVALID_IDENTIFICATION_TYPE",
                ["Invalid IdentificationTypeId."]);
        }

        var duplicatedIdentification = await _unitOfWork.Customers.ExistsByCompanyAndIdentificationExceptIdAsync(
            currentUserService.CompanyId,
            request.Id,
            request.IdentificationTypeId,
            request.IdentificationNumber);

        if (duplicatedIdentification)
        {
            return Response<Guid>.Error(
                "CUSTOMER_IDENTIFICATION_IN_USE",
                ["Another customer already uses the same IdentificationTypeId and IdentificationNumber."]);
        }

        var referenceErrors = new List<string>();

        if (request.Addresses is { Count: > 0 })
        {
            var streetTypeRepository = _unitOfWork.GetRepository<StreetType>();
            var countryRepository = _unitOfWork.GetRepository<Country>();
            var regionRepository = _unitOfWork.GetRepository<Region>();
            var provinceRepository = _unitOfWork.GetRepository<Province>();
            var municipalityRepository = _unitOfWork.GetRepository<Municipality>();

            foreach (var streetTypeId in request.Addresses.Select(a => a.StreetTypeId).Distinct())
            {
                if (await streetTypeRepository.GetAsync(streetTypeId) is null)
                {
                    referenceErrors.Add($"Invalid StreetTypeId in addresses section: {streetTypeId}.");
                }
            }

            foreach (var countryId in request.Addresses.Select(a => a.CountryId).Distinct())
            {
                if (await countryRepository.GetAsync(countryId) is null)
                {
                    referenceErrors.Add($"Invalid CountryId in addresses section: {countryId}.");
                }
            }

            foreach (var provinceId in request.Addresses.Select(a => a.ProvinceId).Distinct())
            {
                if (await provinceRepository.GetAsync(provinceId) is null)
                {
                    referenceErrors.Add($"Invalid ProvinceId in addresses section: {provinceId}.");
                }
            }

            foreach (var municipalityId in request.Addresses.Select(a => a.MunicipalityId).Distinct())
            {
                if (await municipalityRepository.GetAsync(municipalityId) is null)
                {
                    referenceErrors.Add($"Invalid MunicipalityId in addresses section: {municipalityId}.");
                }
            }

            foreach (var regionId in request.Addresses
                .Select(a => a.RegionId)
                .Where(r => r.HasValue)
                .Select(r => r!.Value)
                .Distinct())
            {
                if (regionId == Guid.Empty || await regionRepository.GetAsync(regionId) is null)
                {
                    referenceErrors.Add($"Invalid RegionId in addresses section: {regionId}.");
                }
            }
        }

        if (request.Contacts is { Count: > 0 })
        {
            var invalidContactTypes = request.Contacts
                .Select(c => c.ContactType)
                .Where(ct => !Enum.TryParse<ContactType>(ct, true, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            referenceErrors.AddRange(
                invalidContactTypes.Select(ct => $"Invalid ContactType in contacts section: {ct}."));
        }

        if (referenceErrors.Count != 0)
        {
            return Response<Guid>.Error("INVALID_REFERENCES", referenceErrors);
        }

        _customerMapper.ApplyUpdate(request, customerEntity);

        var collectionSyncErrors = new List<string>();
        SyncAddresses(request, customerEntity, currentUserService.CompanyId, collectionSyncErrors);
        SyncContacts(request, customerEntity, currentUserService.CompanyId, collectionSyncErrors);

        if (collectionSyncErrors.Count != 0)
        {
            return Response<Guid>.Error("INVALID_COLLECTION_ITEMS", collectionSyncErrors);
        }

        await _unitOfWork.Customers.UpdateAsync(customerEntity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "UPDATE_FAILED",
                ["No records were affected while updating the customer."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Data = customerEntity.Id,
            Message = "Customer updated successfully."
        };
    }

    /// <summary>
    /// Synchronizes addresses collection applying add, update, and soft-delete operations.
    /// </summary>
    /// <param name="request">Incoming update command.</param>
    /// <param name="customerEntity">Tracked customer aggregate.</param>
    /// <param name="companyId">Current tenant company identifier.</param>
    private void SyncAddresses(
        UpdateCustomerCommand request,
        Customer customerEntity,
        Guid companyId,
        List<string> errors)
    {
        var incomingAddresses = request.Addresses;
        var incomingAddressIds = incomingAddresses
            .Where(a => a.Id.HasValue && a.Id.Value != Guid.Empty)
            .Select(a => a.Id!.Value)
            .ToHashSet();

        var deletedAtUtc = DateTime.UtcNow;

        foreach (var existingAddress in customerEntity.Addresses.Where(a => a.GcRecord == 0))
        {
            if (!incomingAddressIds.Contains(existingAddress.Id))
            {
                existingAddress.MarkAsDeleted(deletedAtUtc);
            }
        }

        foreach (var incomingAddress in incomingAddresses)
        {
            if (!incomingAddress.Id.HasValue || incomingAddress.Id.Value == Guid.Empty)
            {
                var newAddress = _customerMapper.ToAddressEntity(incomingAddress);
                newAddress.CompanyId = companyId;
                newAddress.CustomerId = customerEntity.Id;
                customerEntity.Addresses.Add(newAddress);
                continue;
            }

            var existingAddress = customerEntity.Addresses
                .FirstOrDefault(a => a.Id == incomingAddress.Id.Value);

            if (existingAddress is null)
            {
                errors.Add($"Address id '{incomingAddress.Id.Value}' does not belong to the current customer.");
                continue;
            }

            _customerMapper.ApplyUpdate(incomingAddress, existingAddress);
            existingAddress.CompanyId = companyId;
            existingAddress.CustomerId = customerEntity.Id;
            existingAddress.GcRecord = 0;
        }
    }

    /// <summary>
    /// Synchronizes contacts collection applying add, update, and soft-delete operations.
    /// </summary>
    /// <param name="request">Incoming update command.</param>
    /// <param name="customerEntity">Tracked customer aggregate.</param>
    /// <param name="companyId">Current tenant company identifier.</param>
    private void SyncContacts(
        UpdateCustomerCommand request,
        Customer customerEntity,
        Guid companyId,
        List<string> errors)
    {
        var incomingContacts = request.Contacts;
        var incomingContactIds = incomingContacts
            .Where(c => c.Id.HasValue && c.Id.Value != Guid.Empty)
            .Select(c => c.Id!.Value)
            .ToHashSet();

        var deletedAtUtc = DateTime.UtcNow;

        foreach (var existingContact in customerEntity.Contacts.Where(c => c.GcRecord == 0))
        {
            if (!incomingContactIds.Contains(existingContact.Id))
            {
                existingContact.MarkAsDeleted(deletedAtUtc);
            }
        }

        foreach (var incomingContact in incomingContacts)
        {
            if (!incomingContact.Id.HasValue || incomingContact.Id.Value == Guid.Empty)
            {
                var newContact = _customerMapper.ToContactEntity(incomingContact);
                newContact.CompanyId = companyId;
                newContact.CustomerId = customerEntity.Id;
                customerEntity.Contacts.Add(newContact);
                continue;
            }

            var existingContact = customerEntity.Contacts
                .FirstOrDefault(c => c.Id == incomingContact.Id.Value);

            if (existingContact is null)
            {
                errors.Add($"Contact id '{incomingContact.Id.Value}' does not belong to the current customer.");
                continue;
            }

            _customerMapper.ApplyUpdate(incomingContact, existingContact);
            existingContact.CompanyId = companyId;
            existingContact.CustomerId = customerEntity.Id;
            existingContact.GcRecord = 0;
        }
    }
}
