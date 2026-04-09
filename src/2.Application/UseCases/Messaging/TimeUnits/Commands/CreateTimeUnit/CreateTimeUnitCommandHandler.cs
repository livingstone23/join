using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Commands;

/// <summary>
/// Handles time unit creation commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class CreateTimeUnitCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<CreateTimeUnitCommand, Response<TimeUnitDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Creates a time unit catalog item.
    /// </summary>
    public async Task<Response<TimeUnitDto>> Handle(CreateTimeUnitCommand request, CancellationToken cancellationToken)
    {
        var timeUnitRepository = _unitOfWork.GetRepository<TimeUnit>();

        var normalizedName = request.Name.Trim();
        var existingTimeUnits = await timeUnitRepository.GetAllAsync();

        var nameInUse = existingTimeUnits.Any(tu =>
            tu.GcRecord == 0
            && string.Equals(tu.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<TimeUnitDto>.Error("TIME_UNIT_NAME_IN_USE", ["Another active time unit already uses the same name."]);
        }

        var codeInUse = existingTimeUnits.Any(tu =>
            tu.GcRecord == 0
            && tu.Code == request.Code);

        if (codeInUse)
        {
            return Response<TimeUnitDto>.Error("TIME_UNIT_CODE_IN_USE", ["Another active time unit already uses the same code."]);
        }

        var entity = new TimeUnit
        {
            Name = normalizedName,
            Code = request.Code,
            IsActive = request.IsActive
        };

        await timeUnitRepository.InsertAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TimeUnitDto>.Error("CREATE_FAILED", ["No records were affected while creating the time unit."]);
        }

        return new Response<TimeUnitDto>
        {
            IsSuccess = true,
            Message = "Time unit created successfully.",
            Data = new TimeUnitDto
            {
                Id = entity.Id,
                Name = entity.Name,
                Code = entity.Code,
                IsActive = entity.IsActive,
                CreatedAt = entity.Created
            }
        };
    }
}
