using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Application.UseCases.Security.SystemOptions;
using JOIN.Domain.Security;
using MediatR;



namespace JOIN.Application.UseCases.Admin.SystemModules.Commands;



/// <summary>
/// Handles soft delete operations for global system modules.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteSystemModuleCommandHandler(
    IUnitOfWork unitOfWork,
    SystemOptionCascadeCoordinator cascadeCoordinator)
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
        var entity = await systemModuleRepository.GetAsync(request.Id);

        if (entity is null || entity.GcRecord != 0)
        {
            return Response<Guid>.Error(
                "SYSTEM_MODULE_NOT_FOUND",
                ["System module not found."]);
        }

        // SPEC 41 (decision 2026-10-08): the module's options are composition (deleted with it, every level);
        // company modules and role grants on any of those options are references and block the delete.
        var options = await cascadeCoordinator.GetActiveSubtreeAsync(entity.Id, rootOptionId: null);
        var references = new ActiveDependentsCheck(_unitOfWork);
        await references.CountAsync<CompanyModule>(m => m.GcRecord == 0 && m.ModuleId == request.Id, "company modules");
        var blocking = references.Details.Concat(await cascadeCoordinator.GetBlockingReferencesAsync(options.Select(o => o.Id).ToList())).ToList();

        if (blocking.Count > 0)
        {
            return Response<Guid>.Error("SYSTEM_MODULE_IN_USE", blocking);
        }

        var deletedAtUtc = DateTime.UtcNow;
        entity.MarkAsDeleted(deletedAtUtc);
        await cascadeCoordinator.MarkAsDeletedAsync(options, deletedAtUtc);

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