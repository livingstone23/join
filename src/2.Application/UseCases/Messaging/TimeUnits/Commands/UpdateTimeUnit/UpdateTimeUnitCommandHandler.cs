using JOIN.Application.Common;
using JOIN.Application.DTO.Messaging;
using JOIN.Application.Interface.Persistence;
using JOIN.Domain.Messaging;
using MediatR;

namespace JOIN.Application.UseCases.Messaging.TimeUnits.Commands;

/// <summary>
/// Handles time unit update commands.
/// </summary>
/// <param name="unitOfWork">Unit of work used for transactional persistence.</param>
public sealed class UpdateTimeUnitCommandHandler(IUnitOfWork unitOfWork)
    : IRequestHandler<UpdateTimeUnitCommand, Response<TimeUnitDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    /// <summary>
    /// Updates a time unit catalog item.
    /// </summary>
    public async Task<Response<TimeUnitDto>> Handle(UpdateTimeUnitCommand request, CancellationToken cancellationToken)
    {
        var timeUnitRepository = _unitOfWork.GetRepository<TimeUnit>();

        var entity = await timeUnitRepository.GetAsync(request.Id);
        if (entity is null)
        {
            return Response<TimeUnitDto>.Error("TIME_UNIT_NOT_FOUND", ["Time unit not found."]);
        }

        var normalizedName = request.Name.Trim();
        var existingTimeUnits = await timeUnitRepository.GetAllAsync();

        var nameInUse = existingTimeUnits.Any(tu =>
            tu.Id != request.Id
            && tu.GcRecord == 0
            && string.Equals(tu.Name, normalizedName, StringComparison.OrdinalIgnoreCase));

        if (nameInUse)
        {
            return Response<TimeUnitDto>.Error("TIME_UNIT_NAME_IN_USE", ["Another active time unit already uses the same name."]);
        }

        var codeInUse = existingTimeUnits.Any(tu =>
            tu.Id != request.Id
            && tu.GcRecord == 0
            && tu.Code == request.Code);

        if (codeInUse)
        {
            return Response<TimeUnitDto>.Error("TIME_UNIT_CODE_IN_USE", ["Another active time unit already uses the same code."]);
        }

        entity.Name = normalizedName;
        entity.Code = request.Code;
        entity.IsActive = request.IsActive;

        await timeUnitRepository.UpdateAsync(entity);
        var result = await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (result <= 0)
        {
            return Response<TimeUnitDto>.Error("UPDATE_FAILED", ["No records were affected while updating the time unit."]);
        }

        return new Response<TimeUnitDto>
        {
            IsSuccess = true,
            Message = "Time unit updated successfully.",
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
