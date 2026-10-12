using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.Mappings;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarStatuses.Commands.CreateCalendarStatus;

/// <summary>
/// Handles <see cref="CreateCalendarStatusCommand"/>: the code is unique among active statuses.
/// </summary>
public sealed class CreateCalendarStatusCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    ICalendarMapper mapper)
    : IRequestHandler<CreateCalendarStatusCommand, Response<CalendarStatusDto>>
{
    public async Task<Response<CalendarStatusDto>> Handle(CreateCalendarStatusCommand request, CancellationToken cancellationToken)
    {
        if (currentUserService.CompanyId == Guid.Empty)
        {
            return Response<CalendarStatusDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var repository = unitOfWork.GetRepository<CalendarStatus>();
        var code = CalendarCatalogRules.NormalizeCode(request.Code);
        if ((await repository.GetAllIncludingDeletedAsync(x => x.Code == code && x.GcRecord == 0)).Any())
        {
            return Response<CalendarStatusDto>.Error(CalendarErrorCodes.StatusCodeDuplicate, ["Another active status already uses this code."]);
        }

        var entity = new CalendarStatus
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = CalendarCatalogRules.Clean(request.Description),
            Color = request.Color.Trim(),
            BlocksTime = request.BlocksTime,
            IsFinal = request.IsFinal,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder
        };

        await repository.InsertAsync(entity);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CalendarStatusDto> { IsSuccess = true, Message = "Calendar status created successfully.", Data = mapper.ToDto(entity) };
    }
}
