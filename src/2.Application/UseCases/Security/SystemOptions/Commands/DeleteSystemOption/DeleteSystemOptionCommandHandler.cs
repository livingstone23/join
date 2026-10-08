using System.Threading;
using System.Threading.Tasks;
using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using MediatR;

namespace JOIN.Application.UseCases.Security.SystemOptions.Commands;

/// <summary>
/// Handler for soft deleting a SystemOption.
/// </summary>
/// <summary>
/// Handles soft delete operations for SystemOption.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class DeleteSystemOptionCommandHandler(
    IUnitOfWork unitOfWork,
    SystemOptionCascadeCoordinator cascadeCoordinator)
    : IRequestHandler<DeleteSystemOptionCommand, Response<Guid>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Performs a logical delete by marking the SystemOption as removed.
    /// </summary>
    public async Task<Response<Guid>> Handle(DeleteSystemOptionCommand request, CancellationToken cancellationToken)
    {
        var optionRepository = _unitOfWork.GetRepository<JOIN.Domain.Security.SystemOption>();

        var entity = await optionRepository.GetAsync(request.Id);
        if (entity is null)
        {
            return Response<Guid>.Error("SYSTEM_OPTION_NOT_FOUND", ["System option not found."]);
        }

        // SPEC 41 (decision 2026-10-08): child options are composition (deleted with the option, every level);
        // role grants on the option or any descendant are references and block the delete.
        var descendants = await cascadeCoordinator.GetActiveSubtreeAsync(entity.ModuleId, entity.Id);
        var blocking = await cascadeCoordinator.GetBlockingReferencesAsync(descendants.Select(o => o.Id).Append(entity.Id).ToList());

        if (blocking.Count > 0)
        {
            return Response<Guid>.Error("SYSTEM_OPTION_IN_USE", blocking);
        }

        var deletedAtUtc = DateTime.UtcNow;
        entity.MarkAsDeleted(deletedAtUtc);
        await cascadeCoordinator.MarkAsDeletedAsync(descendants, deletedAtUtc);
        await optionRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<Guid>.Error("DELETE_FAILED", ["No records were affected while deleting the system option."]);
        }

        return new Response<Guid>
        {
            IsSuccess = true,
            Message = "System option deleted successfully.",
            Data = entity.Id
        };
    }
}
