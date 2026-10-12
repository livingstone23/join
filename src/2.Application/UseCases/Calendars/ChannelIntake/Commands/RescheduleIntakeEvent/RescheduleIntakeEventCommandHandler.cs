using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.RescheduleIntakeEvent;

/// <summary>
/// Handles <see cref="RescheduleIntakeEventCommand"/> (SPEC 45) through <see cref="CalendarEventCommandProcessor"/>
/// as a panel change: R4 + R5 apply because the agent is never the owner, and the activity takes the company's
/// <c>RescheduledByOthersStatusCompanyId</c> (R6). The log row carries the requester's channel and identifier and, in
/// its notes, the <see cref="ReferenceMarker"/> that makes a retry idempotent.
/// </summary>
public sealed class RescheduleIntakeEventCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    CalendarCompanyGuard companyGuard,
    IntakeChannelReader channelReader,
    IntakeEventGuard eventGuard,
    CalendarEventCommandProcessor processor)
    : IRequestHandler<RescheduleIntakeEventCommand, Response<RescheduleIntakeEventDto>>
{
    /// <summary> Prefix of the log notes that records the message id of the reschedule. </summary>
    public static string ReferenceMarker(string reference) => $"[ref:{reference.Trim()}]";

    public async Task<Response<RescheduleIntakeEventDto>> Handle(RescheduleIntakeEventCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<RescheduleIntakeEventDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var company = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!company.IsSuccess)
        {
            return Response<RescheduleIntakeEventDto>.Error(company.Message, company.Errors);
        }

        var channel = await channelReader.FindActiveAsync(request.OriginChannelCode);
        if (channel is null)
        {
            return Response<RescheduleIntakeEventDto>.Error(CalendarErrorCodes.IntakeInvalidChannel,
                [$"The channel '{request.OriginChannelCode}' is not an active agent channel."]);
        }

        var target = await eventGuard.OpenAsync(companyId, request.EventId, request.RequesterPersonId);
        if (!target.IsSuccess)
        {
            return Response<RescheduleIntakeEventDto>.Error(target.Message, target.Errors);
        }

        var marker = ReferenceMarker(request.OriginReference);
        var applied = (await unitOfWork.GetRepository<CalendarEventLog>()
                .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId && x.ChannelId == channel.Id && x.Notes != null && x.Notes.StartsWith(marker)))
            .FirstOrDefault();
        if (applied is not null)
        {
            var current = await unitOfWork.CalendarEvents.GetAsync(applied.CalendarEventId) ?? target.Data!;
            var state = await processor.BuildResultAsync(current, company.Data!, "The reschedule was already applied; the current activity is returned.");
            return Map(state);
        }

        var calendarEvent = target.Data!;
        var input = IntakeEventInput.MovedFrom(calendarEvent, request.Start, request.End, company.Data!.TimeZone);
        var notes = string.IsNullOrWhiteSpace(request.Notes) ? marker : $"{marker} {request.Notes.Trim()}";
        var origin = new CalendarExternalOrigin(channel.Id, IntakeChannels.OriginTypeOf(channel.Code), request.OriginIdentifier.Trim(), null);

        var result = await processor.UpdateAsync(
            calendarEvent.Id,
            IntakeEventGuard.DefaultScope(calendarEvent, request.Scope),
            request.OccurrenceStartUtc,
            input,
            panel: true,
            cancellationToken,
            origin,
            notes);

        return Map(result);
    }

    private static Response<RescheduleIntakeEventDto> Map(Response<CalendarEventDto> result) =>
        result.IsSuccess
            ? new Response<RescheduleIntakeEventDto>
            {
                IsSuccess = true,
                Data = new RescheduleIntakeEventDto(
                    result.Data!.Id,
                    result.Data.StartUtc,
                    result.Data.EndUtc,
                    new IntakeStatusDto(result.Data.Status.Id, result.Data.Status.Code, result.Data.Status.Name, result.Data.Status.Color)),
                Message = result.Message
            }
            : Response<RescheduleIntakeEventDto>.Error(result.Message, result.Errors);
}
