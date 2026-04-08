using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.Areas.Commands;

/// <summary>
/// Handles soft delete operations for tenant-scoped areas.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteAreaCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteAreaCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the area as removed.
    /// </summary>
    /// <param name="request">The delete payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the deleted area identifier.</returns>
    public async Task<Response<Guid>> Handle(DeleteAreaCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<Guid>.Error("INVALID_COMPANY_ID", ["The X-Company-Id header is required."]);
        }

        var areaRepository = _unitOfWork.GetRepository<Area>();
        var entity = await areaRepository.GetAsync(request.Id);

        if (entity is null || entity.CompanyId != request.CompanyId || entity.GcRecord != 0)
        {
            return Response<Guid>.Error("AREA_NOT_FOUND", ["Area not found."]);
        }

        entity.MarkAsDeleted();

        await areaRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the area."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Area deleted successfully.",
            Data = entity.Id
        };
    }
}
