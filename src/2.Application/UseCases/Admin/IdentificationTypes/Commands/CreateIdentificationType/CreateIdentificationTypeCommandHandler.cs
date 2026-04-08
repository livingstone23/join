using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.IdentificationTypes.Commands;

/// <summary>
/// Handles identification type creation commands using the transactional write stack.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class CreateIdentificationTypeCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateIdentificationTypeCommand, Response<IdentificationTypeDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a new identification type after validating uniqueness.
    /// </summary>
    /// <param name="request">The creation payload.</param>
    /// <param name="cancellationToken">The operation cancellation token.</param>
    /// <returns>A standardized response describing the outcome of the create operation.</returns>
    public async Task<Response<IdentificationTypeDto>> Handle(CreateIdentificationTypeCommand request, CancellationToken cancellationToken)
    {
        var identificationTypeRepository = _unitOfWork.GetRepository<IdentificationType>();

        var normalizedName = request.Name.Trim();
        var normalizedDescription = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        var normalizedValidationPattern = string.IsNullOrWhiteSpace(request.ValidationPattern) ? null : request.ValidationPattern.Trim();

        var existingIdentificationTypes = await identificationTypeRepository.GetAllAsync();
        var nameInUse = existingIdentificationTypes.Any(identificationType =>
            identificationType.GcRecord == 0
            && string.Equals(identificationType.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<IdentificationTypeDto>.Error(
                "IDENTIFICATION_TYPE_NAME_IN_USE",
                ["Another active identification type already uses the same name."]);
        }

        var entity = new IdentificationType
        {
            Name = normalizedName,
            Description = normalizedDescription,
            ValidationPattern = normalizedValidationPattern,
            IsActive = request.IsActive
        };

        await identificationTypeRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<IdentificationTypeDto>.Error(
                "CREATE_FAILED",
                ["No records were affected while creating the identification type."]);
        }

        return new Response<IdentificationTypeDto>
        {
            IsSuccess = true,
            Message = "Identification type created successfully.",
            Data = new IdentificationTypeDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Description = entity.Description,
                ValidationPattern = entity.ValidationPattern,
                IsActive = entity.IsActive,
                CreatedAt = entity.Created
            }
        };
    }
}