using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.Regions.Commands;

/// <summary>
/// Handles soft delete operations for regions.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteRegionCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteRegionCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the region as removed.
    /// </summary>
    /// <param name="request">The delete payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the deleted region id.</returns>
    public async Task<Response<Guid>> Handle(DeleteRegionCommand request, CancellationToken cancellationToken)
    {
        var regionRepository = _unitOfWork.GetRepository<Region>();
        var provinceRepository = _unitOfWork.GetRepository<Province>();

        var regionEntity = await regionRepository.GetAsync(request.Id);
        if (regionEntity is null)
        {
            return Response<Guid>.Error("REGION_NOT_FOUND", ["Region not found."]);
        }

        var provinces = await provinceRepository.GetAllAsync();
        var isInUse = provinces.Any(province =>
            province.GcRecord == 0
            && province.RegionId == request.Id);

        if (isInUse)
        {
            return Response<Guid>.Error("REGION_IN_USE", ["The region is currently linked to active provinces and cannot be deleted."]);
        }

        regionEntity.MarkAsDeleted();

        await regionRepository.UpdateAsync(regionEntity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the region."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Region deleted successfully.",
            Data = regionEntity.Id
        };
    }
}
