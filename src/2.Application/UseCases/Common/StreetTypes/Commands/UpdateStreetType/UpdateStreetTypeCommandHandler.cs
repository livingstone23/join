using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.StreetTypes.Commands;

/// <summary>
/// Handles street type update commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class UpdateStreetTypeCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateStreetTypeCommand, Response<StreetTypeDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates an existing street type.
    /// </summary>
    public async Task<Response<StreetTypeDto>> Handle(UpdateStreetTypeCommand request, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.GetRepository<StreetType>();
        var entity = await repository.GetAsync(request.Id);

        if (entity is null)
        {
            return Response<StreetTypeDto>.Error("STREETTYPE_NOT_FOUND", ["Street type not found."]);
        }

        var normalizedName = request.Name.Trim();
        var normalizedAbbreviation = request.Abbreviation.Trim();

        var streetTypes = await repository.GetAllAsync();
        var nameInUse = streetTypes.Any(s => s.Id != request.Id && string.Equals(s.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (nameInUse)
        {
            return Response<StreetTypeDto>.Error("STREETTYPE_NAME_IN_USE", ["Another active street type already uses the same name."]);
        }

        var abbreviationInUse = streetTypes.Any(s => s.Id != request.Id && string.Equals(s.Abbreviation, normalizedAbbreviation, StringComparison.OrdinalIgnoreCase));
        if (abbreviationInUse)
        {
            return Response<StreetTypeDto>.Error("STREETTYPE_ABBREVIATION_IN_USE", ["Another active street type already uses the same abbreviation."]);
        }

        entity.Name = normalizedName;
        entity.Abbreviation = normalizedAbbreviation;
        entity.IsActive = request.IsActive;

        await repository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<StreetTypeDto>.Error("UPDATE_FAILED", ["No records were affected while updating the street type."]);
        }

        return new Response<StreetTypeDto>
        {
            IsSuccess = true,
            Message = "Street type updated successfully.",
            Data = new StreetTypeDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Abbreviation = entity.Abbreviation,
                IsActive = entity.IsActive
            }
        };
    }
}
