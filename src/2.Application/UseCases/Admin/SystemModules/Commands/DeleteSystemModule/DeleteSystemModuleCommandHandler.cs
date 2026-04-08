using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Security;
using MediatR;

namespace JOIN.Application.UseCases.Admin.SystemModules.Commands;

/// <summary>
/// Handles soft delete operations for global system modules.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteSystemModuleCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteSystemModuleCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the system module as removed.
    /// </summary>
    /// <param name="request">The delete payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response containing the deleted system module identifier.</returns>
    public async Task<Response<Guid>> Handle(DeleteSystemModuleCommand request, CancellationToken cancellationToken)
    {
        var systemModuleRepository = _unitOfWork.GetRepository<SystemModule>();
        var systemOptionRepository = _unitOfWork.GetRepository<SystemOption>();
        var entity = await systemModuleRepository.GetAsync(request.Id);

        if (entity is null || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(
                "SYSTEM_MODULE_NOT_FOUND",
                ["System module not found."]);
        }

        var systemOptions = await systemOptionRepository.GetAllAsync();
        var isInUse = systemOptions.Any(option => option.GcRecord == 0 && option.ModuleId == request.Id);

        if (isInUse)
        {
            return Response<Guid>.Error(
                "SYSTEM_MODULE_IN_USE",
                ["The system module is currently linked to one or more system options and cannot be deleted."]);
        }

        entity.MarkAsDeleted();

        await systemModuleRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<Guid>.Error(
                "DELETE_FAILED",
                ["No records were affected while deleting the system module."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "System module deleted successfully.",
            Data = entity.Id
        };
    }
}