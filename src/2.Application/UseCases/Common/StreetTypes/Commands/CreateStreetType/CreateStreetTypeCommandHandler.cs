using JOIN.Application.Common;
using JOIN.Application.DTO.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Common;
using MediatR;

namespace JOIN.Application.UseCases.Common.StreetTypes.Commands;

/// <summary>
/// Handles street type creation commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public class CreateStreetTypeCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateStreetTypeCommand, Response<StreetTypeDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a street type.
    /// </summary>
    public async Task<Response<StreetTypeDto>> Handle(CreateStreetTypeCommand request, CancellationToken cancellationToken)
    {
        var repository = _unitOfWork.GetRepository<StreetType>();
        var normalizedName = request.Name.Trim();
        var normalizedAbbreviation = request.Abbreviation.Trim();

        var streetTypes = await repository.GetAllAsync();
        var nameInUse = streetTypes.Any(s => string.Equals(s.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (nameInUse)
        {
            return Response<StreetTypeDto>.Error("STREETTYPE_NAME_IN_USE", ["Another active street type already uses the same name."]);
        }

        var abbreviationInUse = streetTypes.Any(s => string.Equals(s.Abbreviation, normalizedAbbreviation, StringComparison.OrdinalIgnoreCase));
        if (abbreviationInUse)
        {
            return Response<StreetTypeDto>.Error("STREETTYPE_ABBREVIATION_IN_USE", ["Another active street type already uses the same abbreviation."]);
        }

        var entity = new StreetType
        {
            Name = normalizedName,
            Abbreviation = normalizedAbbreviation,
            IsActive = request.IsActive
        };

        await repository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);
        if (result <= 0)
        {
            return Response<StreetTypeDto>.Error("CREATE_FAILED", ["No records were affected while creating the street type."]);
        }

        return new Response<StreetTypeDto>
        {
            IsSuccess = true,
            Message = "Street type created successfully.",
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
