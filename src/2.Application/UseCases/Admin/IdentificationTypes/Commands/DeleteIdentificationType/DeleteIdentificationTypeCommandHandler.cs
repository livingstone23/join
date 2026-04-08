using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;

/// <summary>
/// Handles soft delete operations for identification document types.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteIdentificationTypeCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteIdentificationTypeCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by inactivating and marking the identification type as removed.
    /// </summary>
    /// <param name="request">The delete payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the deleted identification type identifier.</returns>
    public async Task<Response<Guid>> Handle(DeleteIdentificationTypeCommand request, CancellationToken cancellationToken)
    {
        var identificationTypeRepository = _unitOfWork.GetRepository<IdentificationType>();
        var entity = await identificationTypeRepository.GetAsync(request.Id);

        if (entity is null || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(
                "IDENTIFICATION_TYPE_NOT_FOUND",
                ["Identification type not found."]);
        }

        entity.IsActive = false;
        entity.MarkAsDeleted();

        await identificationTypeRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the identification type."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Identification type deleted successfully.",
            Data = entity.Id
        };
    }
}