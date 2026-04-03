using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.StreetTypes.Commands;

/// <summary>
/// Handles soft delete operations for street types.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class DeleteStreetTypeCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteStreetTypeCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking GcRecord.
    /// </summary>
    public async Task<Response<Guid>> Handle(DeleteStreetTypeCommand request, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.GetRepository<StreetType>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null)
        {
            return Response<Guid>.Error("STREETTYPE_NOT_FOUND", ["Street type not found."]);
        }

        entity.GcRecord = 1;

        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the street type."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "Street type deleted successfully.",
            Data = entity.Id
        };
    }
}
