


using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using JOIN.Domain.Enums;
using MediatR;



namespace JOIN.Application.UseCases.Admin.Persons.Commands;



/// <summary>
/// Handles the creation of a new Person, ensuring transactional integrity.
/// </summary>
/// <param name="unitOfWork">Unit of Work used for command-side persistence and reference validation.</param>
/// <param name="currentUserService">Current user context used to enforce tenant isolation.</param>
public class CreatePersonCommandHandler(
    IUnitOfWork unitOfWork,
    IPersonMapper customerMapper,
    ICurrentUserService currentUserService) : IRequestHandler<CreatePersonCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;
    private readonly IPersonMapper _customerMapper = customerMapper;

    /// <summary>
    /// Creates a customer after validating tenant context and foreign key references.
    /// </summary>
    /// <param name="request">The command containing the customer payload.</param>
    /// <param name="cancellationToken">A cancellation token for the async workflow.</param>
    /// <returns>A standard response with the created customer identifier when successful.</returns>
    public async Task<Response<Guid>> Handle(CreatePersonCommand request, CancellationToken cancellationToken)
    {
        var response = new Response<Guid>();

        if (currentUserService.CompanyId == Guid.Empty)
        {
            response.IsSuccess = false;
            response.Message = "CompanyId header or claim is required.";
            response.Errors = ["The X-Company-Id header is required."];
            return response;
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var company = await companyRepository.GetAsync(currentUserService.CompanyId);
        if (company is null)
        {
            response.IsSuccess = false;
            response.Message = "The provided company does not exist or is inactive.";
            response.Errors = ["Invalid CompanyId."];
            return response;
        }

        var identificationTypeRepository = _unitOfWork.GetRepository<IdentificationType>();
        var identificationType = await identificationTypeRepository.GetAsync(request.IdentificationTypeId);
        if (identificationType is null)
        {
            response.IsSuccess = false;
            response.Message = "The provided identification type does not exist or is inactive.";
            response.Errors = ["Invalid IdentificationTypeId."];
            return response;
        }

        if (request.PersonType == PersonType.Physical)
        {
            var gender = await _unitOfWork.GetRepository<Gender>().GetAsync(request.GenderId!.Value);
            if (gender is null || gender.CompanyId != currentUserService.CompanyId || gender.GcRecord != 0)
            {
                return Response<Guid>.Error(
                    "INVALID_REFERENCES",
                    [$"GenderId '{request.GenderId}' does not exist in the current tenant."]);
            }
        }

        if (request.Addresses is { Count: > 0 })
        {
            var streetTypeRepository = _unitOfWork.GetRepository<StreetType>();
            var countryRepository = _unitOfWork.GetRepository<Country>();
            var regionRepository = _unitOfWork.GetRepository<Region>();
            var provinceRepository = _unitOfWork.GetRepository<Province>();
            var municipalityRepository = _unitOfWork.GetRepository<Municipality>();
            var referenceErrors = new List<string>();

            var streetTypeIds = request.Addresses.Select(a => a.StreetTypeId).Distinct();
            foreach (var streetTypeId in streetTypeIds)
            {
                if (await streetTypeRepository.GetAsync(streetTypeId) is null)
                {
                    referenceErrors.Add($"Invalid StreetTypeId in addresses section: {streetTypeId}.");
                }
            }

            var countryIds = request.Addresses.Select(a => a.CountryId).Distinct();
            foreach (var countryId in countryIds)
            {
                if (await countryRepository.GetAsync(countryId) is null)
                {
                    referenceErrors.Add($"Invalid CountryId in addresses section: {countryId}.");
                }
            }

            var provinceIds = request.Addresses.Select(a => a.ProvinceId).Distinct();
            foreach (var provinceId in provinceIds)
            {
                if (await provinceRepository.GetAsync(provinceId) is null)
                {
                    referenceErrors.Add($"Invalid ProvinceId in addresses section: {provinceId}.");
                }
            }

            var municipalityIds = request.Addresses.Select(a => a.MunicipalityId).Distinct();
            foreach (var municipalityId in municipalityIds)
            {
                if (await municipalityRepository.GetAsync(municipalityId) is null)
                {
                    referenceErrors.Add($"Invalid MunicipalityId in addresses section: {municipalityId}.");
                }
            }

            var regionIds = request.Addresses
                .Select(a => a.RegionId)
                .Where(r => r.HasValue)
                .Select(r => r!.Value)
                .Distinct();

            foreach (var regionId in regionIds)
            {
                if (regionId == Guid.Empty || await regionRepository.GetAsync(regionId) is null)
                {
                    referenceErrors.Add($"Invalid RegionId in addresses section: {regionId}.");
                }
            }

            if (referenceErrors.Count != 0)
            {
                response.IsSuccess = false;
                response.Message = "One or more address references are invalid.";
                response.Errors = referenceErrors;
                return response;
            }
        }

        if (request.Contacts is { Count: > 0 })
        {
            var invalidContactTypes = request.Contacts
                .Select(c => c.ContactType)
                .Where(ct => !Enum.TryParse<ContactType>(ct, true, out _))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (invalidContactTypes.Count != 0)
            {
                response.IsSuccess = false;
                response.Message = "One or more contact types are invalid.";
                response.Errors = invalidContactTypes
                    .Select(ct => $"Invalid ContactType in contacts section: {ct}.")
                    .ToList();
                return response;
            }
        }

        var customerAlreadyExists = await _unitOfWork.Persons.ExistsByCompanyAndIdentificationAsync(
            currentUserService.CompanyId,
            request.IdentificationNumber);

        if (customerAlreadyExists)
        {
            return Response<Guid>.Error(
                "CUSTOMER_ALREADY_EXISTS",
                ["A customer with the same identification number already exists for this company."]);
        }

        // 1. Map command payload to aggregate root (contacts are attached separately via domain factory).
        var customerEntity = _customerMapper.ToEntity(request);
        customerEntity.CompanyId = currentUserService.CompanyId;

        if (request.PersonType == PersonType.Legal)
        {
            customerEntity.GenderId = null;
        }

        foreach (var address in customerEntity.Addresses)
        {
            address.CompanyId = currentUserService.CompanyId;
            address.PersonId = customerEntity.Id;
        }

        if (request.Contacts is { Count: > 0 })
        {
            var primariesByType = new Dictionary<ContactType, PersonContact>();

            foreach (var contactDto in request.Contacts)
            {
                if (!Enum.TryParse<ContactType>(contactDto.ContactType, true, out var contactType))
                {
                    continue;
                }

                var contact = PersonContact.Create(
                    currentUserService.CompanyId,
                    customerEntity.Id,
                    contactType,
                    contactDto.ContactValue,
                    contactDto.Comments);

                if (contactDto.IsPrimary)
                {
                    if (primariesByType.TryGetValue(contactType, out var existingPrimary))
                    {
                        existingPrimary.RemovePrimary();
                    }

                    contact.SetAsPrimary();
                    primariesByType[contactType] = contact;
                }

                customerEntity.Contacts.Add(contact);
            }
        }

        // 2. Insert the aggregate so EF Core persists Person + child collections atomically.
        await _unitOfWork.Persons.InsertAsync(customerEntity);

        // 3. Commit Transaction (This triggers the AuditableEntitySaveChangesInterceptor)
        var result = await _unitOfWork.SaveAsync(cancellationToken);

        if (result > 0)
        {
            response.Data = customerEntity.Id;
            response.IsSuccess = true;
            response.Message = "Person created successfully.";
        }
        else
        {
            response.IsSuccess = false;
            response.Message = "Failed to create the customer due to a database error.";
        }

        return response;
        
    }
}