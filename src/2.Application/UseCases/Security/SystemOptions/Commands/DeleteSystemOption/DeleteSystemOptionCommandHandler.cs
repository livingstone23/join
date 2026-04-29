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
        var roleOptionRepository = _unitOfWork.GetRepository<JOIN.Domain.Security.RoleSystemOption>();

        var entity = await optionRepository.GetAsync(request.Id);
        if (entity is null)
        {
            return Response<Guid>.Error("SYSTEM_OPTION_NOT_FOUND", ["System option not found."]);
        }

        // Verificar si tiene hijos activos
        var children = await optionRepository.GetAllAsync();
        var hasActiveChildren = children.Any(c => c.ParentId == request.Id && c.GcRecord == 0);
        if (hasActiveChildren)
        {
            return Response<Guid>.Error("SYSTEM_OPTION_IN_USE", ["The system option has active child options and cannot be deleted."]);
        }

        // Verificar si está asignado a roles activos
        var roleOptions = await roleOptionRepository.GetAllAsync();
        var isAssignedToRole = roleOptions.Any(ro => ro.SystemOptionId == request.Id && ro.GcRecord == 0);
        if (isAssignedToRole)
        {
            return Response<Guid>.Error("SYSTEM_OPTION_IN_USE", ["The system option is currently assigned to one or more roles and cannot be deleted."]);
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
