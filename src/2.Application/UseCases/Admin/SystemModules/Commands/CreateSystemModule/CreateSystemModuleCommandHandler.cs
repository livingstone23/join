using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.SystemModules.Commands;

/// <summary>
/// Handles system module creation commands using the transactional write stack.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class CreateSystemModuleCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateSystemModuleCommand, Response<SystemModuleDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a new system module after validating uniqueness.
    /// </summary>
    /// <param name="request">The creation payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response describing the outcome of the create operation.</returns>
    public async Task<Response<SystemModuleDto>> Handle(CreateSystemModuleCommand request, CancellationToken cancellationToken)
    {
        var systemModuleRepository = _unitOfWork.GetRepository<SystemModule>();

        var normalizedName = request.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var normalizedIcon = string.IsNullOrWhiteSpace(request.Icon) ? null : request.Icon.Trim();

        var existingModules = await systemModuleRepository.GetAllAsync();
        var nameInUse = existingModules.Any(module =>
            module.GcRecord == 0
            && string.Equals(module.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<SystemModuleDto>.Error(
                "SYSTEM_MODULE_NAME_IN_USE",
                ["Another active system module already uses the same name."]);
        }

        var entity = new SystemModule
        {
            Name = normalizedName,
            Description = normalizedDescription,
            Icon = normalizedIcon,
            IsActive = request.IsActive
        };

        await systemModuleRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<SystemModuleDto>.Error(
                "CREATE_FAILED",
                ["No records were affected while creating the system module."]);
        }

        return new Response<SystemModuleDto>
        {
            IsSuccess = true,
            Message = "System module created successfully.",
            Data = new SystemModuleDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                Icon = entity.Icon,
                IsActive = entity.IsActive,
                CreatedAt = entity.Created
            }
        };
    }
}