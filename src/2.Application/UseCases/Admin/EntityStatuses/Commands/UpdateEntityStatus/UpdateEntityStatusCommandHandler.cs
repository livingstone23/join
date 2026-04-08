using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Commands;

/// <summary>
/// Handles entity status update commands using the transactional write stack.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class UpdateEntityStatusCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateEntityStatusCommand, Response<EntityStatusDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates an existing entity status after validating uniqueness and request scope.
    /// </summary>
    /// <param name="request">The update payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response describing the outcome of the update operation.</returns>
    public async Task<Response<EntityStatusDto>> Handle(UpdateEntityStatusCommand request, CancellationToken cancellationToken)
    {
        if (request.CompanyId == Guid.Empty)
        {
            return Response<EntityStatusDto>.Error("INVALID_COMPANY_ID", ["The X-Company-Id header is required."]);
        }

        var companyRepository = _unitOfWork.GetRepository<Company>();
        var statusRepository = _unitOfWork.GetRepository<EntityStatus>();

        var company = await companyRepository.GetAsync(request.CompanyId);
        if (company is null)
        {
            return Response<EntityStatusDto>.Error("INVALID_COMPANY_ID", ["The specified CompanyId does not exist."]);
        }

        var entity = await statusRepository.GetAsync(request.Id);
        if (entity is null || entity.GcRecord != 0)
        {
            return Response<EntityStatusDto>.Error("ENTITY_STATUS_NOT_FOUND", ["Entity status not found."]);
        }

        var normalizedName = request.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        var existingStatuses = await statusRepository.GetAllAsync();
        var nameInUse = existingStatuses.Any(status =>
            status.Id != request.Id
            && status.GcRecord == 0
            && string.Equals(status.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<EntityStatusDto>.Error("ENTITY_STATUS_NAME_IN_USE", ["Another active entity status already uses the same name."]);
        }

        var codeInUse = existingStatuses.Any(status =>
            status.Id != request.Id
            && status.GcRecord == 0
            && status.Code == request.Code);

        if (codeInUse)
        {
            return Response<EntityStatusDto>.Error("ENTITY_STATUS_CODE_IN_USE", ["Another active entity status already uses the same code."]);
        }

        entity.Name = normalizedName;
        entity.Description = normalizedDescription;
        entity.Code = request.Code;
        entity.IsOperative = request.IsOperative;

        await statusRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<EntityStatusDto>.Error("UPDATE_FAILED", ["No records were affected while updating the entity status."]);
        }

        return new Response<EntityStatusDto>
        {
            IsSuccess = true,
            Message = "Entity status updated successfully.",
            Data = new EntityStatusDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                Code = entity.Code,
                IsOperative = entity.IsOperative,
                CreatedAt = entity.Created
            }
        };
    }
}
