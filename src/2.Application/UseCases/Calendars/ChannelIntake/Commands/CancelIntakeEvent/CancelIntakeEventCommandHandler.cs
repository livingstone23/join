using JOIN.Application.Common;
using JOIN.Application.DTO.Calendars;
using JOIN.Application.Interface;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Calendars;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.ChannelIntake.Commands.CancelIntakeEvent;

/// <summary>
/// Handles <see cref="CancelIntakeEventCommand"/> (SPEC 45) through
/// <see cref="CalendarEventCommandProcessor.ChangeStatusAsync"/>: the activity (or the occurrence) moves to the
/// company's <c>CANCELLED</c> status and the <c>StatusChanged</c> log row carries the reason, the requester's channel
/// and identifier.
/// </summary>
public sealed class CancelIntakeEventCommandHandler(
    ICurrentUserService currentUserService,
    CalendarCompanyGuard companyGuard,
    CalendarCatalogReader catalogReader,
    IntakeChannelReader channelReader,
    IntakeEventGuard eventGuard,
    CalendarEventCommandProcessor processor)
    : IRequestHandler<CancelIntakeEventCommand, Response<CancelIntakeEventDto>>
{
    public async Task<Response<CancelIntakeEventDto>> Handle(CancelIntakeEventCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<CancelIntakeEventDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        var company = await companyGuard.GetContextAsync(companyId, cancellationToken);
        if (!company.IsSuccess)
        {
            return Response<CancelIntakeEventDto>.Error(company.Message, company.Errors);
        }

        var channel = await channelReader.FindActiveAsync(request.OriginChannelCode);
        if (channel is null)
        {
            return Response<CancelIntakeEventDto>.Error(CalendarErrorCodes.IntakeInvalidChannel,
                [$"The channel '{request.OriginChannelCode}' is not an active agent channel."]);
        }

        var target = await eventGuard.OpenAsync(companyId, request.EventId, request.RequesterPersonId);
        if (!target.IsSuccess)
        {
            return Response<CancelIntakeEventDto>.Error(target.Message, target.Errors);
        }

        var cancelled = await catalogReader.GetCompanyStatusByCodeAsync(companyId, CalendarCodes.Statuses.Cancelled);
        if (cancelled is null)
        {
            return Response<CancelIntakeEventDto>.Error(CalendarErrorCodes.StatusNotEnabled, ["The company has no CANCELLED status."]);
        }

        var origin = new CalendarExternalOrigin(channel.Id, IntakeChannels.OriginTypeOf(channel.Code), request.OriginIdentifier.Trim(), null);
        var result = await processor.ChangeStatusAsync(
            target.Data!.Id,
            IntakeEventGuard.DefaultScope(target.Data, request.Scope),
            request.OccurrenceStartUtc,
            cancelled.Id,
            request.Reason,
            panel: true,
            cancellationToken,
            origin);

        return result.IsSuccess
            ? new Response<CancelIntakeEventDto>
            {
                IsSuccess = true,
                Data = new CancelIntakeEventDto(
                    result.Data!.Id,
                    new IntakeStatusDto(result.Data.Status.Id, result.Data.Status.Code, result.Data.Status.Name, result.Data.Status.Color)),
                Message = "Calendar activity cancelled successfully."
            }
            : Response<CancelIntakeEventDto>.Error(result.Message, result.Errors);
    }
}
