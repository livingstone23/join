using JOIN.Application.Common;
using JOIN.Application.Interface.Persistence;
using JOIN.Application.UseCases.Calendars.Common;
using JOIN.Domain.Admin;
using JOIN.Domain.Calendars;

namespace JOIN.Application.UseCases.Admin.PersonPendingConfirmations;

/// <summary>
/// Closes the review activity of a pending confirmation when it is resolved or linked (SPEC 45): an activity that is
/// still in a non-final status moves to the company's <c>COMPLETED</c> status, with its log row, through
/// <see cref="CalendarEventCommandProcessor"/> (the calendar module writes its own entities).
/// </summary>
public sealed class PersonPendingReviewCloser(
    IUnitOfWork unitOfWork,
    CalendarCatalogReader catalogReader,
    CalendarEventCommandProcessor processor)
{
    /// <summary> Notes of the log row of the closed activity. </summary>
    public const string ClosingNotes = "Resuelta desde personas pendientes";

    /// <summary>
    /// Completes the review activity of <paramref name="pending"/>. Nothing happens when there is no activity, it was
    /// deleted, it is already in a final status or the company has no <c>COMPLETED</c> status.
    /// </summary>
    public async Task<Response<bool>> CloseAsync(PersonPendingConfirmation pending, CancellationToken cancellationToken)
    {
        if (pending.ReviewEventId is not { } reviewEventId)
        {
            return Done();
        }

        var reviewEvent = await unitOfWork.CalendarEvents.GetAsync(reviewEventId);
        if (reviewEvent is null || reviewEvent.GcRecord != 0 || reviewEvent.CompanyId != pending.CompanyId)
        {
            return Done();
        }

        var currentStatus = await unitOfWork.GetRepository<CalendarStatusCompany>().GetAsync(reviewEvent.CalendarStatusCompanyId);
        if (currentStatus is not null && (await catalogReader.GetGlobalStatusAsync(currentStatus))?.IsFinal == true)
        {
            return Done();
        }

        var completed = await catalogReader.GetCompanyStatusByCodeAsync(pending.CompanyId, CalendarCodes.Statuses.Completed);
        if (completed is null)
        {
            return Done();
        }

        var result = await processor.ChangeStatusAsync(reviewEvent.Id, null, null, completed.Id, ClosingNotes, panel: true, cancellationToken);
        return result.IsSuccess ? Done() : Response<bool>.Error(result.Message, result.Errors);
    }

    private static Response<bool> Done() => new() { IsSuccess = true, Data = true };
}
