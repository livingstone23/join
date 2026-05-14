using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonAddresses.Commands;

/// <summary>
/// Handles customer address creation using EF Core repositories through the unit of work.
/// </summary>
public sealed class CreatePersonAddressCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<CreatePersonAddressCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a customer address associated with the authenticated tenant company.
    /// </summary>
    /// <param name="request">The create-address command.</param>
    /// <param name="cancellationToken">A cancellation token for the asynchronous workflow.</param>
    /// <returns>A response containing the created address identifier.</returns>
    public async Task<Response<Guid>> Handle(CreatePersonAddressCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("COMPANY_REQUIRED", ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var customerRepository = _unitOfWork.GetRepository<Person>();
        var customer = await customerRepository.GetAsync(request.PersonId);

        if (customer is null || customer.CompanyId != currentUserService.CompanyId || customer.GcRecord != 0)
        {
            return Response<Guid>.Error("CUSTOMER_NOT_FOUND", ["The requested customer does not exist in the current tenant."]);
        }

        // Validate catalog FK references before attempting INSERT
        var referenceErrors = new List<string>();

        if (await _unitOfWork.GetRepository<Country>().GetAsync(request.CountryId) is null)
            referenceErrors.Add($"CountryId '{request.CountryId}' does not exist.");

        if (await _unitOfWork.GetRepository<StreetType>().GetAsync(request.StreetTypeId) is null)
            referenceErrors.Add($"StreetTypeId '{request.StreetTypeId}' does not exist.");

        if (request.RegionId.HasValue &&
            await _unitOfWork.GetRepository<Region>().GetAsync(request.RegionId.Value) is null)
            referenceErrors.Add($"RegionId '{request.RegionId}' does not exist.");

        if (await _unitOfWork.GetRepository<Province>().GetAsync(request.ProvinceId) is null)
            referenceErrors.Add($"ProvinceId '{request.ProvinceId}' does not exist.");

        if (await _unitOfWork.GetRepository<Municipality>().GetAsync(request.MunicipalityId) is null)
            referenceErrors.Add($"MunicipalityId '{request.MunicipalityId}' does not exist.");

        if (referenceErrors.Count != 0)
        {
            return Response<Guid>.Error("INVALID_REFERENCES", referenceErrors);
        }

        var entity = new PersonAddress
        {
            PersonId = request.PersonId,
            AddressLine1 = request.AddressLine1.Trim(),
            AddressLine2 = request.AddressLine2?.Trim(),
            ZipCode = request.ZipCode.Trim(),
            StreetTypeId = request.StreetTypeId,
            CountryId = request.CountryId,
            RegionId = request.RegionId,
            ProvinceId = request.ProvinceId,
            MunicipalityId = request.MunicipalityId,
            IsDefault = request.IsDefault,
            CompanyId = currentUserService.CompanyId
        };

        var addressRepository = _unitOfWork.GetRepository<PersonAddress>();
        await addressRepository.InsertAsync(entity);

        var result = await _unitOfWork.SaveAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("ADDRESS_CREATE_FAILED", ["The customer address could not be created due to a persistence error."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Person address created successfully.",
            Data = entity.Id
        };
    }
}
