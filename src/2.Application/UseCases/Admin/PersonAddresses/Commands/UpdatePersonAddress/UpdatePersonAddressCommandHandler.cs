using JOIN.Application.Common;
using JOIN.Application.Exceptions;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonAddresses.Commands;

/// <summary>
/// Handles customer address updates using Entity Framework Core through the unit of work.
/// </summary>
public sealed class UpdatePersonAddressCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService) : IRequestHandler<UpdatePersonAddressCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates a customer address for the current tenant.
    /// </summary>
    /// <param name="request">The update-address command.</param>
    /// <param name="cancellationToken">A cancellation token for the asynchronous workflow.</param>
    /// <returns>A response containing the updated address identifier.</returns>
    public async Task<Response<Guid>> Handle(UpdatePersonAddressCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error(
                "COMPANY_REQUIRED",
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var addressRepository = _unitOfWork.GetRepository<PersonAddress>();
        var addresses = await addressRepository.GetAllAsync();

        var entity = addresses.FirstOrDefault(address =>
            address.Id == request.Id &&
            address.CompanyId == currentUserService.CompanyId);

        if (entity is null)
        {
            throw new NotFoundException(
                nameof(PersonAddress),
                request.Id,
                "Person address not found for the current tenant.");
        }

        if (entity.PersonId != request.PersonId)
        {
            throw new NotFoundException(
                nameof(PersonAddress),
                request.Id,
                "Person address not found for the requested customer.");
        }

        entity.AddressLine1 = request.AddressLine1.Trim();
        entity.AddressLine2 = request.AddressLine2?.Trim();
        entity.ZipCode = request.ZipCode.Trim();
        entity.StreetTypeId = request.StreetTypeId;
        entity.CountryId = request.CountryId;
        entity.RegionId = request.RegionId;
        entity.ProvinceId = request.ProvinceId;
        entity.MunicipalityId = request.MunicipalityId;
        entity.IsDefault = request.IsDefault;

        await addressRepository.UpdateAsync(entity);

        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "UPDATE_FAILED",
                ["No records were affected while updating the customer address."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Person address updated successfully.",
            Data = entity.Id
        };
    }
}