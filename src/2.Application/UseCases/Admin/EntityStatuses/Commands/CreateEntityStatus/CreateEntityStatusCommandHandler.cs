using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Admin.EntityStatuses.Commands;

/// <summary>
/// Handles entity status creation commands using the transactional write stack.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class CreateEntityStatusCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateEntityStatusCommand, Response<EntityStatusDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a new entity status after validating uniqueness and request scope.
    /// </summary>
    /// <param name="request">The creation payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response describing the outcome of the create operation.</returns>
    public async Task<Response<EntityStatusDto>> Handle(CreateEntityStatusCommand request, CancellationToken cancellationToken)
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

        var normalizedName = request.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        var existingStatuses = await statusRepository.GetAllAsync();
        var nameInUse = existingStatuses.Any(status =>
            status.GcRecord == 0
            && string.Equals(status.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<EntityStatusDto>.Error("ENTITY_STATUS_NAME_IN_USE", ["Another active entity status already uses the same name."]);
        }

        var codeInUse = existingStatuses.Any(status => status.GcRecord == 0 && status.Code == request.Code);
        if (codeInUse)
        {
            return Response<EntityStatusDto>.Error("ENTITY_STATUS_CODE_IN_USE", ["Another active entity status already uses the same code."]);
        }

        var entity = new EntityStatus
        {
            Name = normalizedName,
            Description = normalizedDescription,
            Code = request.Code,
            IsOperative = request.IsOperative
        };

        await statusRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<EntityStatusDto>.Error("CREATE_FAILED", ["No records were affected while creating the entity status."]);
        }

        return new Response<EntityStatusDto>
        {
            IsSuccess = true,
            Message = "Entity status created successfully.",
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
