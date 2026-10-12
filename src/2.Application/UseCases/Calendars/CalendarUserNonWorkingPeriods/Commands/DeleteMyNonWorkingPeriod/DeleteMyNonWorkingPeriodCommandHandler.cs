using JOIN.Application.Common;
using JOIN.Application.UseCases.Calendars.Common;
using MediatR;

namespace JOIN.Application.UseCases.Calendars.CalendarUserNonWorkingPeriods.Commands.DeleteMyNonWorkingPeriod;

/// <summary>
/// Handles <see cref="DeleteMyNonWorkingPeriodCommand"/>: another user's period answers 404.
/// </summary>
public sealed class DeleteMyNonWorkingPeriodCommandHandler(CalendarNonWorkingPeriodWriter writer)
    : IRequestHandler<DeleteMyNonWorkingPeriodCommand, Response<bool>>
{
    public async Task<Response<bool>> Handle(DeleteMyNonWorkingPeriodCommand request, CancellationToken cancellationToken)
    {
        var company = await writer.OpenAsync(cancellationToken);
        if (!company.IsSuccess)
        {
            return Response<bool>.Error(company.Message, company.Errors);
        }

        if (writer.CurrentUserId is not { } userId)
        {
            return Response<bool>.Error(CalendarErrorCodes.UserRequired, ["The authenticated user could not be resolved."]);
        }

        return await writer.DeleteAsync(company.Data!.CompanyId, request.Id, userId, cancellationToken);
    }
}
