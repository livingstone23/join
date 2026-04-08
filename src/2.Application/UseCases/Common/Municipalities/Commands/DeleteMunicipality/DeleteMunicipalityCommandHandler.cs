using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Municipalities.Commands;

/// <summary>
/// Handles soft delete operations for municipalities.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteMunicipalityCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteMunicipalityCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the municipality as removed.
    /// </summary>
    /// <param name="request">The delete payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the deleted municipality id.</returns>
    public async Task<Response<Guid>> Handle(DeleteMunicipalityCommand request, CancellationToken cancellationToken)
    {
        var municipalityRepository = _unitOfWork.GetRepository<Municipality>();
        var customerAddressRepository = _unitOfWork.GetRepository<CustomerAddress>();

        var municipalityEntity = await municipalityRepository.GetAsync(request.Id);
        if (municipalityEntity is null)
        {
            return Response<Guid>.Error("MUNICIPALITY_NOT_FOUND", ["Municipality not found."]);
        }

        var customerAddresses = await customerAddressRepository.GetAllAsync();
        var isInUse = customerAddresses.Any(address =>
            address.GcRecord == 0
            && address.MunicipalityId == request.Id);

        if (isInUse)
        {
            return Response<Guid>.Error("MUNICIPALITY_IN_USE", ["The municipality is currently linked to customer addresses and cannot be deleted."]);
        }

        municipalityEntity.MarkAsDeleted();

        await municipalityRepository.UpdateAsync(municipalityEntity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the municipality."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Municipality deleted successfully.",
            Data = municipalityEntity.Id
        };
    }
}
