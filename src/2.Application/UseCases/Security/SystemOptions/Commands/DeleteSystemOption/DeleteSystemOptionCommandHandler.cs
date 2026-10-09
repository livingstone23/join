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
public sealed class DeleteSystemOptionCommandHandler(IUnitOfWork unitOfWork)
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

        // Active child options and active role assignments both block the delete (SPEC 41).
        var dependents = new ActiveDependentsCheck(_unitOfWork);
        await dependents.CountAsync<JOIN.Domain.Security.SystemOption>(c => c.GcRecord == 0 && c.ParentId == request.Id, "child system options");
        await dependents.CountAsync<JOIN.Domain.Security.RoleSystemOption>(ro => ro.GcRecord == 0 && ro.SystemOptionId == request.Id, "role system options");

        if (dependents.HasDependents)
        {
            return Response<Guid>.Error("SYSTEM_OPTION_IN_USE", dependents.Details);
        }

        entity.MarkAsDeleted();
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
