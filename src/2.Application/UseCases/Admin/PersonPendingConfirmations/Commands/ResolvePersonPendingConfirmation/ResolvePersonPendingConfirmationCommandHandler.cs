using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;
using JOIN.Domain.Calendars.Enums;
using JOIN.Domain.Enums;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.ResolvePersonPendingConfirmation;

/// <summary>
/// Updates the pending's status, captures the resolver and (when the review event is still open)
/// transitions it to the company's <c>COMPLETED</c> status with a single log row. Discarding does
/// not deactivate the <c>Person</c> (decision 2026-10-01).
/// </summary>
public sealed class ResolvePersonPendingConfirmationCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService)
    : IRequestHandler<ResolvePersonPendingConfirmationCommand, Response<ResolvePersonPendingConfirmationDto>>
{
    public async Task<Response<ResolvePersonPendingConfirmationDto>> Handle(
        ResolvePersonPendingConfirmationCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<ResolvePersonPendingConfirmationDto>.Error(
                CalendarErrorCodes.CompanyRequired,
                ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (request.Status is not (PersonPendingConfirmationStatus.Confirmed or PersonPendingConfirmationStatus.Discarded))
        {
            return Response<ResolvePersonPendingConfirmationDto>.Error(
                "INVALID_RESOLUTION_STATUS",
                ["Resolution status must be Confirmed or Discarded."]);
        }

        if (request.Status == PersonPendingConfirmationStatus.Discarded && string.IsNullOrWhiteSpace(request.Notes))
        {
            return Response<ResolvePersonPendingConfirmationDto>.Error(
                CalendarErrorCodes.IntakeResolutionNotesRequired,
                ["Notes are required when discarding a pending confirmation."]);
        }

        var pending = (await unitOfWork.GetRepository<PersonPendingConfirmation>()
                .GetAllIncludingDeletedAsync(x => x.Id == request.PendingConfirmationId && x.CompanyId == companyId))
            .FirstOrDefault();

        if (pending is null)
        {
            return Response<ResolvePersonPendingConfirmationDto>.Error(
                CalendarErrorCodes.PersonPendingNotFound,
                ["The pending confirmation was not found in the current company."]);
        }

        if (pending.Status != PersonPendingConfirmationStatus.Pending)
        {
            return Response<ResolvePersonPendingConfirmationDto>.Error(
                CalendarErrorCodes.PersonPendingAlreadyResolved,
                ["Only pending confirmations can be resolved."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var resolverId) || resolverId == Guid.Empty)
        {
            return Response<ResolvePersonPendingConfirmationDto>.Error(
                CalendarErrorCodes.UserRequired,
                ["The authenticated user could not be resolved."]);
        }

        pending.Status = request.Status;
        pending.ResolvedByUserId = resolverId;
        pending.ResolvedUtc = DateTime.UtcNow;
        pending.ResolutionNotes = request.Notes?.Trim();
        await unitOfWork.GetRepository<PersonPendingConfirmation>().UpdateAsync(pending);

        if (pending.ReviewEventId.HasValue)
        {
            var reviewEvent = await unitOfWork.CalendarEvents.GetAsync(pending.ReviewEventId.Value);
            if (reviewEvent is not null
                && reviewEvent.CompanyId == companyId
                && reviewEvent.GcRecord == 0)
            {
                var currentStatus = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(reviewEvent.CalendarStatusCompanyId);
                var currentIsFinal = currentStatus is not null
                    && (await unitOfWork.GetRepository<Domain.Calendars.CalendarStatus>().GetAsync(currentStatus.CalendarStatusId)) is { IsFinal: true };

                if (!currentIsFinal)
                {
                    var completedStatus = (await unitOfWork.GetRepository<CalendarStatusCompany>()
                            .GetAllIncludingDeletedAsync(x => x.CompanyId == companyId
                                && x.GcRecord == 0
                                && x.IsActive))
                        .Join(await unitOfWork.GetRepository<Domain.Calendars.CalendarStatus>()
                                .GetAllIncludingDeletedAsync(x => x.GcRecord == 0 && x.IsActive),
                            csc => csc.CalendarStatusId,
                            cs => cs.Id,
                            (csc, cs) => new { csc, cs })
                        .FirstOrDefault(row => row.cs.Code == CalendarCodes.Statuses.Completed)?.csc;

                    if (completedStatus is not null)
                    {
                        var previousStatusId = reviewEvent.CalendarStatusCompanyId;
                        reviewEvent.CalendarStatusCompanyId = completedStatus.Id;
                        reviewEvent.StatusChangedUtc = DateTime.UtcNow;
                        reviewEvent.StatusChangedByUserId = resolverId;
                        await unitOfWork.CalendarEvents.UpdateAsync(reviewEvent);

                        var channel = (await unitOfWork.GetRepository<Domain.Common.CommunicationChannel>()
                                .GetAllIncludingDeletedAsync(x => x.Code == Domain.Common.CommunicationChannelCodes.Web && x.GcRecord == 0))
                            .FirstOrDefault();

                        var log = new CalendarEventLog
                        {
                            CompanyId = companyId,
                            CalendarEventId = reviewEvent.Id,
                            Action = CalendarEventLogAction.StatusChanged,
                            PreviousStatusCompanyId = previousStatusId,
                            NewStatusCompanyId = completedStatus.Id,
                            ChannelId = channel?.Id ?? Guid.Empty,
                            ActorUserId = resolverId,
                            ActorIdentifier = null,
                            Notes = "Resuelta desde personas pendientes",
                            OccurredUtc = DateTime.UtcNow
                        };
                        await unitOfWork.GetRepository<CalendarEventLog>().InsertAsync(log);
                    }
                }
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new Response<ResolvePersonPendingConfirmationDto>
        {
            IsSuccess = true,
            Message = "Person pending confirmation resolved successfully.",
            Data = new ResolvePersonPendingConfirmationDto(pending.Id, (int)pending.Status, pending.ReviewEventId)
        };
    }
}
