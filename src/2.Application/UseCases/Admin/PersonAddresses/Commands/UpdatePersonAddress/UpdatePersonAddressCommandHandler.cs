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
    ICurrentUserService currentUserService,
    PersonAddressDefaultCoordinator defaultCoordinator) : IRequestHandler<UpdatePersonAddressCommand, Response<Guid>>
{
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

        var companyId = currentUserService.CompanyId;
        var addressRepository = unitOfWork.PersonAddresses;
        var entity = await addressRepository.GetActiveByIdAsync(request.Id, companyId, cancellationToken);

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

        try
        {
            if (request.IsDefault)
            {
                await defaultCoordinator.ClearOtherDefaultsAsync(
                    companyId,
                    request.PersonId,
                    entity.Id,
                    cancellationToken);
                entity.SetAsDefault();
            }
            else
            {
                entity.RemoveDefault();
            }
        }
        catch (InvalidOperationException ex)
        {
            return Response<Guid>.Error("INVALID_ADDRESS_DEFAULT", [ex.Message]);
        }

        await addressRepository.UpdateAsync(entity);

        var result = await unitOfWork.SaveChangesAsync(cancellationToken);

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
