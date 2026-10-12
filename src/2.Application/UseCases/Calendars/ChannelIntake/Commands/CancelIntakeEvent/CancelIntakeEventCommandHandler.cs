using JOIN.Application.Common;
using JOIN.Application.Interface;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake;

/// <summary>
/// Handles <see cref="CancelIntakeEventCommand"/> (SPEC 45 F4). Validates ownership, refuses to reopen
/// final-status activities, requires a non-empty <see cref="CancelIntakeEventCommand.Reason"/>, and moves the
/// event to the company status whose global code is <see cref="CalendarCodes.Statuses.Cancelled"/>.
/// </summary>
public sealed class CancelIntakeEventCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanyGuard companyGuard,
    CalendarEventLogWriter logWriter)
    : IRequestHandler<CancelIntakeEventCommand, Response<CancelIntakeEventDto>>
{
    public async Task<Response<CancelIntakeEventDto>> Handle(
        CancelIntakeEventCommand request,
        CancellationToken cancellationToken)
    {
        var companyId = request.CompanyId == Guid.Empty ? currentUserService.CompanyId : request.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CancelIntakeEventDto>.Error(CalendarErrorCodes.CompanyRequired,
                ["The X-Company-Id header or the CompanyId claim is required."]);
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return Response<CancelIntakeEventDto>.Error(CalendarErrorCodes.CancellationReasonRequired,
                ["A reason is required to cancel an activity."]);
        }

        var companyResponse = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!companyResponse.IsSuccess)
        {
            return Response<CancelIntakeEventDto>.Error(companyResponse.Message, companyResponse.Errors);
        }
        var company = companyResponse.Data!;

        var channel = await FindChannelAsync(unitOfWork, request.OriginChannelCode, cancellationToken);
        if (channel is null)
        {
            return Response<CancelIntakeEventDto>.Error(CalendarErrorCodes.IntakeInvalidChannel,
                [$"Channel '{request.OriginChannelCode}' is not registered or is inactive."]);
        }

        var calendarEvent = await unitOfWork.GetRepository<CalendarEvent>().GetAsync(request.EventId);
        if (calendarEvent is null
            || calendarEvent.CompanyId != companyId
            || calendarEvent.GcRecord != 0
            || calendarEvent.RequesterPersonId != request.RequesterPersonId)
        {
            return Response<CancelIntakeEventDto>.Error(CalendarErrorCodes.EventNotFound,
                ["The calendar activity was not found or does not belong to the requester."]);
        }

        var currentStatusCompany = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(calendarEvent.CalendarStatusCompanyId);
        var currentStatusGlobal = currentStatusCompany is null ? null
            : await unitOfWork.GetRepository<Domain.Calendars.CalendarStatus>().GetAsync(currentStatusCompany.CalendarStatusId);
        if (currentStatusGlobal is { IsFinal: true })
        {
            return Response<CancelIntakeEventDto>.Error(CalendarErrorCodes.IntakeEventAlreadyClosed,
                ["The activity is already in a final status and cannot be cancelled again."]);
        }

        // Locate the company status whose global code is CANCELLED.
        var globalCancelled = (await unitOfWork.GetRepository<Domain.Calendars.CalendarStatus>()
                .GetAllIncludingDeletedAsync(s => s.Code == CalendarCodes.Statuses.Cancelled && s.GcRecord == 0 && s.IsActive))
            .FirstOrDefault();
        if (globalCancelled is null)
        {
            return Response<CancelIntakeEventDto>.Error(CalendarErrorCodes.StatusNotEnabled,
                ["The CANCELLED global status is not active."]);
        }

        var cancelledCompanyStatus = (await unitOfWork.GetRepository<CalendarStatusCompany>()
                .GetAllIncludingDeletedAsync(c => c.CompanyId == companyId && c.CalendarStatusId == globalCancelled.Id && c.GcRecord == 0))
            .FirstOrDefault();
        if (cancelledCompanyStatus is null)
        {
            return Response<CancelIntakeEventDto>.Error(CalendarErrorCodes.StatusNotEnabled,
                ["The CANCELLED status is not enabled in the company."]);
        }

        var actorUserId = ParseUserIdOrNull(currentUserService.UserId);
        var previousStatusId = calendarEvent.CalendarStatusCompanyId;
        calendarEvent.CalendarStatusCompanyId = cancelledCompanyStatus.Id;
        calendarEvent.StatusChangedUtc = DateTime.UtcNow;
        calendarEvent.StatusChangedByUserId = actorUserId;
        await unitOfWork.GetRepository<CalendarEvent>().UpdateAsync(calendarEvent);

        var actor = new CalendarActor(channel.Id, actorUserId, request.OriginIdentifier);
        await logWriter.WriteAsync(calendarEvent, CalendarEventLogAction.StatusChanged, actor,
            new CalendarEventLogChange(PreviousStatusCompanyId: previousStatusId, NewStatusCompanyId: cancelledCompanyStatus.Id),
            request.Reason.Trim());

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<CancelIntakeEventDto>
        {
            IsSuccess = true,
            Data = new CancelIntakeEventDto(
                calendarEvent.Id,
                new IntakeStatusDto(
                    cancelledCompanyStatus.Id,
                    globalCancelled.Code,
                    cancelledCompanyStatus.Name,
                    cancelledCompanyStatus.Color)),
            Message = "Calendar activity cancelled."
        };
    }

    private static Guid? ParseUserIdOrNull(string? userId) =>
        Guid.TryParse(userId, out var parsed) && parsed != Guid.Empty ? parsed : null;

    private static async Task<ChannelRow?> FindChannelAsync(
        IUnitOfWork unitOfWork,
        string channelCode,
        CancellationToken cancellationToken)
    {
        var channels = await unitOfWork.GetRepository<Domain.Common.CommunicationChannel>()
            .GetAllIncludingDeletedAsync(c => c.Code == channelCode && c.GcRecord == 0 && c.IsActive);
        var c = channels.FirstOrDefault();
        return c is null ? null : new ChannelRow { Id = c.Id, Code = c.Code ?? string.Empty };
    }

    private sealed class ChannelRow
    {
        public Guid Id { get; init; }
        public string Code { get; init; } = string.Empty;
    }
}