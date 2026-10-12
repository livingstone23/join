using JOIN.Application.Common;
using JOIN.Application.DTO.Admin;
using JOIN.Application.Interface;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using MediatR;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations.Commands.ResolvePersonPendingConfirmation;

/// <summary>
/// Handles <see cref="ResolvePersonPendingConfirmationCommand"/> (SPEC 45): stores the status, who and when, the
/// notes, and completes the review activity (<see cref="PersonPendingReviewCloser"/>). Discarding keeps the person
/// active (decision 2026-10-01).
/// </summary>
public sealed class ResolvePersonPendingConfirmationCommandHandler(
    IUnitOfWork unitOfWork,
    ICurrentUserService currentUserService,
    PersonPendingConfirmationReader reader,
    PersonPendingReviewCloser reviewCloser)
    : IRequestHandler<ResolvePersonPendingConfirmationCommand, Response<ResolvePersonPendingConfirmationDto>>
{
    public async Task<Response<ResolvePersonPendingConfirmationDto>> Handle(ResolvePersonPendingConfirmationCommand request, CancellationToken cancellationToken)
    {
        var companyId = currentUserService.CompanyId;
        if (companyId == Guid.Empty)
        {
            return Response<ResolvePersonPendingConfirmationDto>.Error(CalendarErrorCodes.CompanyRequired, ["The authenticated token must contain a valid CompanyId claim."]);
        }

        if (!Guid.TryParse(currentUserService.UserId, out var userId) || userId == Guid.Empty)
        {
            return Response<ResolvePersonPendingConfirmationDto>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
        }

        var opened = await reader.OpenPendingAsync(companyId, request.PendingConfirmationId);
        if (!opened.IsSuccess)
        {
            return Response<ResolvePersonPendingConfirmationDto>.Error(opened.Message, opened.Errors);
        }

        var pending = opened.Data!;
        pending.Status = request.Status;
        pending.ResolvedByUserId = userId;
        pending.ResolvedUtc = DateTime.UtcNow;
        pending.ResolutionNotes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        await unitOfWork.GetRepository<PersonPendingConfirmation>().UpdateAsync(pending);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var closed = await reviewCloser.CloseAsync(pending, cancellationToken);
        if (!closed.IsSuccess)
        {
            return Response<ResolvePersonPendingConfirmationDto>.Error(closed.Message, closed.Errors);
        }

        return new Response<ResolvePersonPendingConfirmationDto>
        {
            IsSuccess = true,
            Data = new ResolvePersonPendingConfirmationDto(pending.Id, (int)pending.Status, pending.ReviewEventId),
            Message = "Person pending confirmation resolved successfully."
        };
    }
}
